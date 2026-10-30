using Argos.Shared.Contracts;

namespace Argos.Server.Services;

/// <summary>
/// Règles métier d'ADAGIO ODE : appels, ordres de départ, statuts d'engins, journal.
/// Toutes les écritures passent par un verrou unique (pas de double engagement d'un même engin),
/// puis chaque poste est prévenu en temps réel pour recharger son état.
/// </summary>
public sealed class OdeService(OdeDatabase db, RealtimeHub hub, ILogger<OdeService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly Random Rng = new();

    // ===== Lecture =====

    public OdeSnapshot Snapshot()
    {
        EnsureEngins();
        var fleet = Fleet().Where(f => f.Active).ToDictionary(f => f.Nom, StringComparer.OrdinalIgnoreCase);
        var engins = db.Engins()
            .Where(e => fleet.ContainsKey(e.Nom))
            .Select(e => Enrich(e, fleet[e.Nom]))
            .OrderBy(e => fleet[e.Nom].Ordre).ThenBy(e => e.Nom)
            .ToList();
        return new OdeSnapshot
        {
            Calls = db.RecentCalls(300),
            Interventions = db.Interventions(300),
            Engins = engins,
            ServerTime = DateTimeOffset.UtcNow
        };
    }

    public List<JournalEntry> Journal(int limit) => db.Journal(Math.Clamp(limit, 1, 2000));

    public CallRecord? GetCall(string id) => db.GetCall(id);

    public Intervention? GetIntervention(string id) => db.GetIntervention(id);

    // ===== Appels =====

    /// <summary>Nouvelle fiche d'appel (décroché manuel ou SIMCALL).</summary>
    public async Task<CallRecord> CreateCallAsync(ArgosUserDto user, string? callerPhone = null, string? simcallCallId = null,
        DateTimeOffset? answeredAt = null, string? operatorName = null)
    {
        await _gate.WaitAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var call = new CallRecord
            {
                Id = $"{now:yyyyMMdd}-{db.NextCounter("call-" + now.ToString("yyyyMMdd")):0000}",
                Status = CallStatuses.EnCours,
                ReceivedAt = answeredAt ?? now,
                OperatorUserId = user.UserId,
                OperatorName = OperatorLabel(operatorName ?? user.DisplayName),
                SimcallCallId = simcallCallId,
                CallerPhone = callerPhone ?? string.Empty,
                Cstc = EnginsCatalog.Creteil,
                UpdatedAt = now
            };
            call.Events.Add(Event(user, "creation", simcallCallId is null ? "Appel créé." : "Appel 18 décroché (SIMCALL)."));
            db.SaveCall(call);
            Journal(user, "Création d'appel", call.Id, call.CallerPhone);
            await NotifyAsync("call");
            return call;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Enregistre la fiche d'appel. Les champs modifiés sont tracés dans la main courante de l'appel :
    /// les renseignements précédents ne sont jamais perdus.
    /// </summary>
    public async Task<OdeResult> UpdateCallAsync(ArgosUserDto user, CallRecord incoming, string? newStatus)
    {
        await _gate.WaitAsync();
        try
        {
            var call = db.GetCall(incoming.Id);
            if (call is null)
                return new(false, "Appel introuvable.");
            if (call.Status is CallStatuses.SansSuite && newStatus is not null)
                return new(false, "Cet appel est clos.");

            var changes = Diff(call, incoming);
            Copy(incoming, call);
            if (newStatus is not null && newStatus != call.Status && call.Status != CallStatuses.Intervention)
            {
                changes.Add($"statut : {call.Status} → {newStatus}");
                call.Status = newStatus;
            }
            if (changes.Count == 0)
                return new(true, null, call.Id);

            call.UpdatedAt = DateTimeOffset.UtcNow;
            call.Events.Add(Event(user, "modification", string.Join(" ; ", changes)));
            db.SaveCall(call);
            Journal(user, "Modification d'appel", call.Id, string.Join(" ; ", changes));
            await NotifyAsync("call");
            return new(true, null, call.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OdeResult> AddCallEventAsync(ArgosUserDto user, string callId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new(false, "Texte vide.");
        await _gate.WaitAsync();
        try
        {
            var call = db.GetCall(callId);
            if (call is null)
                return new(false, "Appel introuvable.");
            call.Events.Add(Event(user, "renseignement", text.Trim()));
            call.UpdatedAt = DateTimeOffset.UtcNow;
            db.SaveCall(call);
            Journal(user, "Renseignement d'appel", call.Id, text.Trim());
            await NotifyAsync("call");
            return new(true, null, call.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Clôt l'appel sans départ (F5 INU, F9 adresse inexistante, ou classement sans suite).</summary>
    public async Task<OdeResult> CloseWithoutDepartureAsync(ArgosUserDto user, string callId, string procedure, string reason)
    {
        await _gate.WaitAsync();
        try
        {
            var call = db.GetCall(callId);
            if (call is null)
                return new(false, "Appel introuvable.");
            if (call.Status == CallStatuses.Intervention)
                return new(false, "Cet appel a déjà donné lieu à un départ.");

            var label = Procedures.Find(procedure)?.Label ?? "SANS SUITE";
            call.Procedure = procedure;
            call.ClosingReason = string.IsNullOrWhiteSpace(reason) ? label : reason.Trim();
            call.Status = CallStatuses.SansSuite;
            call.EndedAt ??= DateTimeOffset.UtcNow;
            call.UpdatedAt = DateTimeOffset.UtcNow;
            call.Events.Add(Event(user, "cloture", $"Classé sans suite ({label}) : {call.ClosingReason}"));
            db.SaveCall(call);
            Journal(user, "Classement sans suite", call.Id, $"{label} - {call.ClosingReason}");
            await NotifyAsync("call");
            return new(true, null, call.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Rattache un appel à une intervention déjà ouverte (plusieurs appels pour une même intervention).</summary>
    public async Task<OdeResult> LinkCallAsync(ArgosUserDto user, string callId, string interventionId)
    {
        await _gate.WaitAsync();
        try
        {
            var call = db.GetCall(callId);
            var intervention = db.GetIntervention(interventionId);
            if (call is null || intervention is null)
                return new(false, "Appel ou intervention introuvable.");

            call.InterventionId = intervention.Id;
            call.Status = CallStatuses.Intervention;
            call.UpdatedAt = DateTimeOffset.UtcNow;
            call.Events.Add(Event(user, "rattachement", $"Rattaché à l'intervention {intervention.Number}."));
            if (!intervention.CallIds.Contains(call.Id))
                intervention.CallIds.Add(call.Id);
            intervention.Events.Add(Event(user, "appel", $"Appel {call.Id} rattaché ({call.CallerPhone})."));
            intervention.UpdatedAt = DateTimeOffset.UtcNow;
            db.SaveCall(call);
            db.SaveIntervention(intervention);
            Journal(user, "Rattachement d'appel", intervention.Number, call.Id);
            await NotifyAsync("call");
            return new(true, null, intervention.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Appels et interventions d'une période (sans la limite des 300 derniers de l'état courant).</summary>
    public OdeHistory History(DateTimeOffset from, DateTimeOffset to)
    {
        const int limit = 5000;
        var calls = db.CallsBetween(from, to, limit);
        var interventions = db.InterventionsBetween(from, to, limit);
        return new OdeHistory { Calls = calls, Interventions = interventions, Truncated = calls.Count >= limit || interventions.Count >= limit };
    }

    /// <summary>
    /// Change la procédure d'une intervention en cours. Autorisé à l'auteur de l'intervention
    /// et aux rôles d'encadrement (Encadrement, Administration, RH/ADMIN). L'ancienne valeur reste dans l'historique.
    /// </summary>
    public async Task<OdeResult> ChangeProcedureAsync(ArgosUserDto user, string interventionId, ProcedureChangeRequest request)
    {
        var procedure = Procedures.Find(request.Procedure);
        if (procedure is null || !procedure.Departure)
            return new(false, "Procédure inconnue (seules les procédures de départ s'appliquent à une intervention).");
        await _gate.WaitAsync();
        try
        {
            var intervention = db.GetIntervention(interventionId);
            if (intervention is null) return new(false, "Intervention introuvable.");
            if (!intervention.Active) return new(false, "Intervention clôturée : procédure figée.");
            var allowed = intervention.CreatedBy == user.DisplayName || AccessLevels.IsRhAdmin(user.Levels)
                          || user.Levels.Contains(AccessLevels.Encadrement);
            if (!allowed) return new(false, "Seuls l'auteur de l'intervention et l'encadrement peuvent changer la procédure.");
            if (intervention.Procedure == procedure.Code) return new(true, null, intervention.Id);

            var before = Procedures.Find(intervention.Procedure)?.Label ?? intervention.Procedure;
            intervention.Procedure = procedure.Code;
            intervention.UpdatedAt = DateTimeOffset.UtcNow;
            var detail = $"Procédure : {before} → {procedure.Label}" + (string.IsNullOrWhiteSpace(request.Reason) ? "" : $" ({request.Reason.Trim()})");
            intervention.Events.Add(Event(user, "procedure", detail));
            db.SaveIntervention(intervention);
            Journal(user, "Changement de procédure", intervention.Number, detail);
            await NotifyAsync("intervention");
            return new(true, null, intervention.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ===== Ordres de départ =====

    /// <summary>Crée une intervention et son premier ODE, ou un départ complémentaire sur une intervention existante.</summary>
    public async Task<(OdeResult Result, Intervention? Intervention)> CreateOdeAsync(ArgosUserDto user, CreateOdeRequest request)
    {
        await _gate.WaitAsync();
        try
        {
            EnsureEngins();
            var states = db.Engins().ToDictionary(e => e.Nom, StringComparer.OrdinalIgnoreCase);
            var fleetActive = Fleet().Where(f => f.Active).Select(f => f.Nom).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var engins = new List<string>();
            if (!string.IsNullOrWhiteSpace(request.FirstEngin))
                engins.Add(request.FirstEngin.Trim());
            engins.AddRange(request.ComplementaryEngins.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()));
            engins = engins.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (engins.Count == 0)
                return (new(false, "Sélectionne au moins un engin."), null);
            foreach (var nom in engins)
            {
                if (!states.TryGetValue(nom, out var state) || !fleetActive.Contains(nom))
                    return (new(false, $"Engin inconnu : {nom}."), null);
                if (!EnginStatuses.IsAvailable(state.Status))
                    return (new(false, $"{nom} n'est pas disponible ({state.Status})."), null);
            }

            var procedure = Procedures.Find(request.Procedure)?.Code ?? "BLANCHE";
            var now = DateTimeOffset.UtcNow;
            Intervention intervention;
            var isNew = string.IsNullOrWhiteSpace(request.ExistingInterventionId);

            if (isNew)
            {
                if (string.IsNullOrWhiteSpace(request.NatureCode) || Natures.Find(request.NatureCode) is null)
                    return (new(false, "Choisis la nature de l'intervention."), null);
                if (string.IsNullOrWhiteSpace(request.Address))
                    return (new(false, "L'adresse d'intervention est obligatoire."), null);

                intervention = new Intervention
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Number = NewInterventionNumber(),
                    CreatedAt = now,
                    CreatedBy = user.DisplayName,
                    NatureCode = request.NatureCode.Trim(),
                    Procedure = procedure,
                    Secteur = request.Secteur.Trim(),
                    Cstc = string.IsNullOrWhiteSpace(request.Cstc) ? EnginsCatalog.Creteil : request.Cstc.Trim().ToUpperInvariant(),
                    Address = request.Address.Trim(),
                    Locality = request.Locality.Trim(),
                    Building = request.Building.Trim(),
                    Access = request.Access.Trim(),
                    CallerPhone = request.CallerPhone.Trim(),
                    CallerInfo = request.CallerInfo.Trim(),
                    Consignes = request.Consignes.Trim(),
                    Motif = request.Motif.Trim()
                };
                intervention.Events.Add(Event(user, "creation", $"Intervention créée : {Natures.Find(intervention.NatureCode)?.Display}."));
            }
            else
            {
                intervention = db.GetIntervention(request.ExistingInterventionId!)!;
                if (intervention is null || !intervention.Active)
                    return (new(false, "Intervention introuvable ou clôturée."), null);
            }

            var order = new DepartureOrder
            {
                Number = intervention.Orders.Count + 1,
                ValidatedAt = now,
                OperatorName = OperatorLabel(user.DisplayName),
                Procedure = procedure,
                Engins = engins,
                Consignes = request.Consignes.Trim()
            };
            intervention.Orders.Add(order);

            foreach (var nom in engins)
            {
                var first = isNew && string.Equals(nom, engins[0], StringComparison.OrdinalIgnoreCase);
                intervention.Engins.RemoveAll(e => string.Equals(e.Nom, nom, StringComparison.OrdinalIgnoreCase));
                intervention.Engins.Add(new EngagedEngin
                {
                    Nom = states[nom].Nom,
                    FirstDeparture = first,
                    Status = EnginStatuses.Alerte,
                    AlertedAt = now,
                    StatusAt = now,
                    OrderNumber = order.Number
                });
                var state = states[nom];
                state.Status = EnginStatuses.Alerte;
                state.InterventionNumber = intervention.Number;
                state.UpdatedAt = now;
                state.UpdatedBy = user.DisplayName;
                db.SaveEngin(state);
            }

            intervention.Events.Add(Event(user, "depart",
                (isNew ? "Premier départ" : $"Départ complémentaire n°{order.Number}") +
                $" ({Procedures.Find(procedure)?.Label}) : {string.Join(", ", engins)}."));

            if (!string.IsNullOrWhiteSpace(request.CallId))
            {
                var call = db.GetCall(request.CallId!);
                if (call is not null)
                {
                    call.InterventionId = intervention.Id;
                    call.Status = CallStatuses.Intervention;
                    call.Procedure = Procedures.Find(procedure)?.Key ?? call.Procedure;
                    call.EndedAt ??= now;
                    call.UpdatedAt = now;
                    call.Events.Add(Event(user, "ode", $"ODE validé : intervention {intervention.Number}."));
                    db.SaveCall(call);
                    if (!intervention.CallIds.Contains(call.Id))
                        intervention.CallIds.Add(call.Id);
                }
            }

            intervention.UpdatedAt = now;
            db.SaveIntervention(intervention);
            Journal(user, isNew ? "Création d'ODE" : "Départ complémentaire", intervention.Number,
                $"{Procedures.Find(procedure)?.Label} - {string.Join(", ", engins)}");

            await NotifyAsync("ode");
            await hub.BroadcastAsync(new RealtimeMessage(RealtimeTypes.Notify,
                Title: $"ODE {intervention.Number}",
                Text: $"{Natures.Find(intervention.NatureCode)?.Display} - {intervention.Address}\n{string.Join(", ", engins)}",
                AppId: ArgosApps.AdagioOde));
            return (new(true, null, intervention.Id), intervention);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OdeResult> MarkPrintedAsync(ArgosUserDto user, string interventionId, int orderNumber)
    {
        await _gate.WaitAsync();
        try
        {
            var intervention = db.GetIntervention(interventionId);
            var order = intervention?.Orders.FirstOrDefault(o => o.Number == orderNumber);
            if (intervention is null || order is null)
                return new(false, "Ordre de départ introuvable.");
            order.PrintCount++;
            intervention.UpdatedAt = DateTimeOffset.UtcNow;
            db.SaveIntervention(intervention);
            Journal(user, order.PrintCount > 1 ? "Réimpression d'ODE" : "Impression d'ODE", intervention.Number, $"ODE n°{order.Number}");
            return new(true, null, intervention.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ===== Suivi =====

    public async Task<OdeResult> SetEnginStatusAsync(ArgosUserDto user, string interventionId, string nom, string status)
    {
        if (!EnginStatuses.Operationnels.Contains(status))
            return new(false, "Statut inconnu.");
        await _gate.WaitAsync();
        try
        {
            var intervention = db.GetIntervention(interventionId);
            var engaged = intervention?.Engins.FirstOrDefault(e => string.Equals(e.Nom, nom, StringComparison.OrdinalIgnoreCase));
            if (intervention is null || engaged is null)
                return new(false, "Engin non engagé sur cette intervention.");

            var now = DateTimeOffset.UtcNow;
            var previous = engaged.Status;
            engaged.Status = status;
            engaged.StatusAt = now;
            intervention.Events.Add(Event(user, "statut", $"{engaged.Nom} : {previous} → {status}."));
            intervention.UpdatedAt = now;
            db.SaveIntervention(intervention);

            var state = db.Engins().FirstOrDefault(e => string.Equals(e.Nom, engaged.Nom, StringComparison.OrdinalIgnoreCase));
            if (state is not null)
            {
                // « Rentré » libère l'engin (ou le remet dans son état REMISE) ;
                // « Disponible sur intervention » le rend engageable ailleurs.
                state.Status = status == EnginStatuses.Rentre ? IdleStatus(engaged.Nom) : status;
                state.InterventionNumber = status == EnginStatuses.Rentre ? null : intervention.Number;
                state.UpdatedAt = now;
                state.UpdatedBy = user.DisplayName;
                db.SaveEngin(state);
            }

            Journal(user, "Changement de statut", intervention.Number, $"{engaged.Nom} : {previous} → {status}");
            await NotifyAsync("statut");
            return new(true, null, intervention.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OdeResult> AddInterventionMessageAsync(ArgosUserDto user, string interventionId, string text, string kind)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new(false, "Message vide.");
        await _gate.WaitAsync();
        try
        {
            var intervention = db.GetIntervention(interventionId);
            if (intervention is null)
                return new(false, "Intervention introuvable.");
            intervention.Events.Add(Event(user, kind, text.Trim()));
            intervention.UpdatedAt = DateTimeOffset.UtcNow;
            db.SaveIntervention(intervention);
            Journal(user, kind == "renfort" ? "Demande de renfort" : "Message opérationnel", intervention.Number, text.Trim());
            await NotifyAsync("message");
            return new(true, null, intervention.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OdeResult> CloseInterventionAsync(ArgosUserDto user, string interventionId)
    {
        await _gate.WaitAsync();
        try
        {
            var intervention = db.GetIntervention(interventionId);
            if (intervention is null)
                return new(false, "Intervention introuvable.");
            if (!intervention.Active)
                return new(false, "Intervention déjà clôturée.");

            var now = DateTimeOffset.UtcNow;
            var states = db.Engins();
            foreach (var engaged in intervention.Engins)
            {
                var state = states.FirstOrDefault(s => string.Equals(s.Nom, engaged.Nom, StringComparison.OrdinalIgnoreCase));
                if (state is not null && state.InterventionNumber == intervention.Number)
                {
                    state.Status = IdleStatus(state.Nom);
                    state.InterventionNumber = null;
                    state.UpdatedAt = now;
                    state.UpdatedBy = user.DisplayName;
                    db.SaveEngin(state);
                }
                if (engaged.Status != EnginStatuses.Rentre)
                {
                    engaged.Status = EnginStatuses.Rentre;
                    engaged.StatusAt = now;
                }
            }

            intervention.Active = false;
            intervention.ClosedAt = now;
            intervention.UpdatedAt = now;
            intervention.Events.Add(Event(user, "cloture", "Intervention clôturée."));
            db.SaveIntervention(intervention);
            Journal(user, "Clôture d'intervention", intervention.Number, string.Empty);
            await NotifyAsync("cloture");
            return new(true, null, intervention.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ===== SIMCALL =====

    public async Task<CallRecord> OnSimcallAnsweredAsync(SimcallCallAnswered message, ArgosUserDto operatorUser)
    {
        var existing = db.FindCallBySimcallId(message.SimcallCallId);
        if (existing is not null)
            return existing;
        var call = await CreateCallAsync(operatorUser, message.CallerNumber, message.SimcallCallId, message.AnsweredAt, message.OperatorName);
        await hub.SendToUserAsync(message.DiscordUserId, new RealtimeMessage(RealtimeTypes.OpenCall, Text: call.Id));
        return call;
    }

    public async Task OnSimcallEndedAsync(SimcallCallEnded message)
    {
        await _gate.WaitAsync();
        try
        {
            var call = db.FindCallBySimcallId(message.SimcallCallId);
            if (call is null || call.EndedAt is not null)
                return;
            call.EndedAt = message.EndedAt;
            call.UpdatedAt = DateTimeOffset.UtcNow;
            call.Events.Add(new OdeEvent { At = message.EndedAt, Author = "SIMCALL", Kind = "fin", Text = "Fin de l'appel (raccroché)." });
            db.SaveCall(call);
        }
        finally
        {
            _gate.Release();
        }
        await NotifyAsync("call");
    }

    // ===== Outils =====

    // ===== Lien ADAGIO REMISE =====

    /// <summary>Parc d'engins (ADAGIO REMISE) ; créé depuis la feuille GDM au premier démarrage.</summary>
    public List<FleetEngin> Fleet()
    {
        if (!db.AnyDoc(OdeDatabase.RemiseTables.Engins))
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var engin in RemiseSeed.Engins(now))
                db.SaveDoc(OdeDatabase.RemiseTables.Engins, engin.Nom, now, engin);
            if (!db.AnyDoc(OdeDatabase.RemiseTables.Items))
                foreach (var item in RemiseSeed.Items(now))
                    db.SaveDoc(OdeDatabase.RemiseTables.Items, item.Id, now, item);
        }
        return db.Docs<FleetEngin>(OdeDatabase.RemiseTables.Engins).OrderBy(f => f.Ordre).ThenBy(f => f.Nom).ToList();
    }

    /// <summary>
    /// Disponibilité administrative fixée dans REMISE, répercutée sur le statut ODE de l'engin.
    /// Refusée pour un engin engagé si elle le rendrait indisponible.
    /// </summary>
    public async Task<OdeResult> ApplyAvailabilityAsync(ArgosUserDto user, string nom, string availability)
    {
        await _gate.WaitAsync();
        try
        {
            EnsureEngins();
            var state = db.Engins().FirstOrDefault(e => string.Equals(e.Nom, nom, StringComparison.OrdinalIgnoreCase));
            if (state is null)
                return new(false, $"Engin inconnu : {nom}.");
            if (EnginStatuses.IsEngaged(state.Status) || state.Status == EnginStatuses.DisponibleIntervention)
            {
                if (availability != FleetAvailability.Disponible)
                    return new(false, $"{state.Nom} est engagé sur l'intervention {state.InterventionNumber} ({state.Status}) : " +
                                      "il passera indisponible à son retour.");
                return new(true);
            }

            var target = ToOdeStatus(availability);
            if (state.Status == target)
                return new(true);
            state.Status = target;
            state.InterventionNumber = null;
            state.UpdatedAt = DateTimeOffset.UtcNow;
            state.UpdatedBy = user.DisplayName;
            db.SaveEngin(state);
            await NotifyAsync("remise");
            return new(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Changement d'indicatif ou de centre dans REMISE : l'état ODE suit le nouveau nom.</summary>
    public async Task<OdeResult> RenameEnginAsync(string oldNom, string newNom)
    {
        await _gate.WaitAsync();
        try
        {
            var state = db.Engins().FirstOrDefault(e => string.Equals(e.Nom, oldNom, StringComparison.OrdinalIgnoreCase));
            if (state is null)
                return new(true);
            if (EnginStatuses.IsEngaged(state.Status))
                return new(false, $"{oldNom} est engagé : renomme-le à son retour.");
            db.RenameEngin(oldNom, new EnginState
            {
                Nom = newNom, Status = state.Status, UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = state.UpdatedBy
            });
            await NotifyAsync("remise");
            return new(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string ToOdeStatus(string availability) => availability switch
    {
        FleetAvailability.Indisponible => EnginStatuses.Indisponible,
        FleetAvailability.Maintenance => EnginStatuses.Maintenance,
        _ => EnginStatuses.Disponible
    };

    /// <summary>Statut d'un engin qui rentre : celui fixé par REMISE (indisponible, maintenance) ou disponible.</summary>
    private string IdleStatus(string nom)
    {
        var fleet = db.GetDoc<FleetEngin>(OdeDatabase.RemiseTables.Engins, nom);
        return ToOdeStatus(fleet?.Availability ?? FleetAvailability.Disponible);
    }

    private static EnginState Enrich(EnginState state, FleetEngin fleet)
    {
        state.Type = fleet.Type;
        state.Libelle = fleet.Libelle;
        state.Centre = fleet.Centre;
        state.Code = fleet.Code;
        state.Availability = fleet.Availability;
        return state;
    }

    /// <summary>Crée l'état ODE des engins du parc qui n'en ont pas encore.</summary>
    private void EnsureEngins()
    {
        var known = db.Engins().Select(e => e.Nom).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var engin in Fleet().Where(e => !known.Contains(e.Nom)))
            db.SaveEngin(new EnginState
            {
                Nom = engin.Nom, Status = ToOdeStatus(engin.Availability), UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = "ARGOS"
            });
    }

    /// <summary>Numéro d'intervention « 210-593 », unique.</summary>
    private string NewInterventionNumber()
    {
        for (var i = 0; i < 1000; i++)
        {
            var number = $"{Rng.Next(100, 1000)}-{Rng.Next(0, 1000):000}";
            if (!db.InterventionNumberExists(number))
                return number;
        }
        throw new InvalidOperationException("Impossible de générer un numéro d'intervention.");
    }

    /// <summary>Nom d'opérateur affiché comme dans ADAGIO : en majuscules.</summary>
    private static string OperatorLabel(string name) => name.Trim().ToUpperInvariant();

    private static OdeEvent Event(ArgosUserDto user, string kind, string text)
        => new() { At = DateTimeOffset.UtcNow, Author = OperatorLabel(user.DisplayName), Kind = kind, Text = text };

    private void Journal(ArgosUserDto user, string action, string target, string details)
    {
        try
        {
            db.AddJournal(new JournalEntry
            {
                At = DateTimeOffset.UtcNow,
                UserId = user.UserId,
                UserName = user.DisplayName,
                Action = action,
                Target = target,
                Details = details
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Journal ODE : écriture impossible");
        }
    }

    private Task NotifyAsync(string reason)
        => hub.BroadcastAsync(new RealtimeMessage(RealtimeTypes.OdeChanged, Text: reason));

    /// <summary>Champs de la fiche d'appel modifiables par l'opérateur (le reste est géré par le serveur).</summary>
    private static readonly (string Label, Func<CallRecord, string> Get, Action<CallRecord, string> Set)[] EditableFields =
    [
        ("origine", c => c.Origin, (c, v) => c.Origin = v),
        ("n° requérant", c => c.CallerPhone, (c, v) => c.CallerPhone = v),
        ("n° impliqué", c => c.ImplicatedPhone, (c, v) => c.ImplicatedPhone = v),
        ("requérant", c => c.CallerName, (c, v) => c.CallerName = v),
        ("mode", c => c.LocationMode, (c, v) => c.LocationMode = v),
        ("réseau", c => c.LocationNetwork, (c, v) => c.LocationNetwork = v),
        ("numéro", c => c.StreetNumber, (c, v) => c.StreetNumber = v),
        ("voie", c => c.Street, (c, v) => c.Street = v),
        ("CSTC", c => c.Cstc, (c, v) => c.Cstc = v),
        ("adresse", c => c.AlertAddress, (c, v) => c.AlertAddress = v),
        ("commune", c => c.Locality, (c, v) => c.Locality = v),
        ("bâtiment", c => c.Building, (c, v) => c.Building = v),
        ("accès", c => c.Access, (c, v) => c.Access = v),
        ("observations", c => c.Observations, (c, v) => c.Observations = v),
        ("nature présumée", c => c.PresumedNatureCode, (c, v) => c.PresumedNatureCode = v),
        ("nature définitive", c => c.FinalNatureCode, (c, v) => c.FinalNatureCode = v),
        ("nature", c => c.NatureCode, (c, v) => c.NatureCode = v),
        ("motif", c => c.Reason, (c, v) => c.Reason = v),
        ("priorité", c => c.Priority, (c, v) => c.Priority = v),
        ("risques", c => c.Risks, (c, v) => c.Risks = v),
        ("consignes", c => c.Instructions, (c, v) => c.Instructions = v),
        ("victimes", c => c.VictimCount.ToString(), (c, v) => c.VictimCount = int.TryParse(v, out var n) ? Math.Max(0, n) : 0)
    ];

    private static List<string> Diff(CallRecord current, CallRecord incoming)
    {
        var changes = new List<string>();
        foreach (var (label, get, _) in EditableFields)
        {
            var before = get(current) ?? string.Empty;
            var after = (get(incoming) ?? string.Empty).Trim();
            if (!string.Equals(before, after, StringComparison.Ordinal))
                changes.Add(string.IsNullOrEmpty(before) ? $"{label} : {Shorten(after)}" : $"{label} : {Shorten(before)} → {Shorten(after)}");
        }
        if (incoming.LocationConfirmed && !current.LocationConfirmed)
            changes.Add("localisation confirmée");
        return changes;
    }

    private static void Copy(CallRecord from, CallRecord to)
    {
        foreach (var (_, get, set) in EditableFields)
            set(to, (get(from) ?? string.Empty).Trim());
        to.LocationConfirmed = to.LocationConfirmed || from.LocationConfirmed;
    }

    private static string Shorten(string value) => value.Length <= 60 ? value : value[..57] + "…";
}
