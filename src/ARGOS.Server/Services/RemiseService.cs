using Argos.Shared.Contracts;
using static Argos.Server.Services.OdeDatabase;

namespace Argos.Server.Services;

/// <summary>
/// Règles métier d'ADAGIO REMISE : parc d'engins, matériel, vérifications, maintenance, stocks.
/// Les changements de disponibilité passent par <see cref="OdeService"/> pour rester synchronisés avec l'ODE.
/// Toute action est tracée dans le journal d'activité commun (non modifiable).
/// </summary>
public sealed class RemiseService(OdeDatabase db, OdeService ode, RealtimeHub hub)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Gestion du parc : rôle Discord ADAGIO REMISE. Sans configuration par application,
    /// opérateurs, encadrement et administration.
    /// </summary>
    public bool CanManage(ArgosUserDto user)
        => user.Levels.Any(l => l.StartsWith("app:", StringComparison.Ordinal))
            ? user.Levels.Contains(AccessLevels.App(ArgosApps.AdagioRemise))
            : user.Levels.Any(l => l is AccessLevels.Operateur or AccessLevels.Encadrement or AccessLevels.Administration);

    // ===== Lecture =====

    public RemiseSnapshot Snapshot(ArgosUserDto user) => new()
    {
        Engins = ode.Fleet(),
        Items = db.Docs<MaterielItem>(RemiseTables.Items).OrderBy(i => i.Category).ThenBy(i => i.Name).ToList(),
        Verifications = db.Docs<Verification>(RemiseTables.Verifications, 300),
        Maintenances = db.Docs<MaintenanceRecord>(RemiseTables.Maintenances, 500),
        Movements = db.Docs<StockMovement>(RemiseTables.Movements, 500),
        CanManage = CanManage(user),
        ServerTime = DateTimeOffset.UtcNow
    };

    // ===== Engins =====

    public async Task<OdeResult> SaveEnginAsync(ArgosUserDto user, SaveEnginRequest request)
    {
        if (!CanManage(user))
            return new(false, "Habilitation insuffisante pour modifier le parc.");
        var e = request.Engin;
        e.Indicatif = (e.Indicatif ?? string.Empty).Trim().ToUpperInvariant();
        e.Type = (e.Type ?? string.Empty).Trim().ToUpperInvariant();
        e.TypeGdm = (e.TypeGdm ?? string.Empty).Trim().ToUpperInvariant();
        e.Centre = (e.Centre ?? string.Empty).Trim().ToUpperInvariant();
        e.Code = (e.Code ?? string.Empty).Trim().ToUpperInvariant();
        e.Immatriculation = (e.Immatriculation ?? string.Empty).Trim().ToUpperInvariant();
        e.Libelle = (e.Libelle ?? string.Empty).Trim();
        if (e.Indicatif.Length == 0 || e.Type.Length == 0 || e.Centre.Length == 0)
            return new(false, "Indicatif, type et centre de secours sont obligatoires.");
        if (e.ImageData.Length > 1_500_000)
            return new(false, "Image trop lourde (1 Mo maximum).");
        if (!FleetAvailability.All.Contains(e.Availability))
            return new(false, "Disponibilité inconnue.");
        e.Nom = FleetEngin.BuildNom(e.Indicatif, e.Centre);
        if (e.Code.Length == 0)
            e.Code = $"{e.Type.Replace(" ", string.Empty)}_1_{e.Centre}";
        e.Dotation = e.Dotation.Where(d => !string.IsNullOrWhiteSpace(d.ItemId) && d.Required >= 0 && d.Present >= 0)
            .GroupBy(d => d.ItemId).Select(g => g.First()).ToList();

        await _gate.WaitAsync();
        try
        {
            ode.Fleet(); // parc créé depuis la feuille GDM au premier usage
            var fleet = ode.Fleet();
            var originalNom = string.IsNullOrWhiteSpace(request.OriginalNom) ? null : request.OriginalNom.Trim();
            var existing = originalNom is null ? null : fleet.FirstOrDefault(f => Same(f.Nom, originalNom));
            if (originalNom is not null && existing is null)
                return new(false, "Engin introuvable.");
            if (fleet.Any(f => Same(f.Nom, e.Nom) && !ReferenceEquals(f, existing)))
                return new(false, $"{e.Nom} existe déjà dans le parc.");

            var now = DateTimeOffset.UtcNow;
            e.UpdatedAt = now;
            e.UpdatedBy = user.DisplayName;

            if (existing is null)
            {
                e.CreatedAt = now;
                e.AvailabilitySince = now;
                e.Ordre = fleet.Count == 0 ? 0 : fleet.Max(f => f.Ordre) + 10;
                if (e.Dotation.Count == 0)
                    e.Dotation = RemiseSeed.DotationFor(e.Type);
                db.SaveDoc(RemiseTables.Engins, e.Nom, now, e);
                await ode.ApplyAvailabilityAsync(user, e.Nom, e.Availability);
                Journal(user, RemiseJournal.EnginCree, e.Nom, $"{e.TypeLabel} - {e.Centre}");
                await NotifyAsync();
                return new(true, null, e.Nom);
            }

            // Renommage : l'état ODE suit, refusé si l'engin est engagé.
            if (!Same(existing.Nom, e.Nom))
            {
                var renamed = await ode.RenameEnginAsync(existing.Nom, e.Nom);
                if (!renamed.Ok)
                    return renamed;
                db.DeleteDoc(RemiseTables.Engins, existing.Nom);
            }

            var changes = Diff(existing, e);
            e.CreatedAt = existing.CreatedAt;
            e.Ordre = existing.Ordre;
            e.AvailabilitySince = existing.AvailabilitySince;
            string? warning = null;
            if (e.Availability != existing.Availability)
            {
                var applied = await ode.ApplyAvailabilityAsync(user, e.Nom, e.Availability);
                if (!applied.Ok)
                {
                    // L'engin est engagé : la fiche est enregistrée, la disponibilité reste inchangée.
                    warning = applied.Error;
                    e.Availability = existing.Availability;
                    e.AvailabilityReason = existing.AvailabilityReason;
                    changes.RemoveAll(c => c.StartsWith("disponibilité", StringComparison.Ordinal));
                }
                else
                {
                    e.AvailabilitySince = now;
                    Journal(user, RemiseJournal.Disponibilite, e.Nom,
                        $"{existing.Availability} → {e.Availability}{Reason(e.AvailabilityReason)}");
                }
            }
            db.SaveDoc(RemiseTables.Engins, e.Nom, now, e);
            if (changes.Count > 0)
                Journal(user, RemiseJournal.EnginModifie, e.Nom, string.Join(" ; ", changes));
            await NotifyAsync();
            return new(warning is null, warning, e.Nom);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ===== Matériel =====

    public async Task<OdeResult> SaveItemAsync(ArgosUserDto user, MaterielItem item)
    {
        if (!CanManage(user))
            return new(false, "Habilitation insuffisante pour modifier le matériel.");
        item.Name = (item.Name ?? string.Empty).Trim();
        item.Category = (item.Category ?? string.Empty).Trim();
        item.Unit = string.IsNullOrWhiteSpace(item.Unit) ? "unité" : item.Unit.Trim();
        if (item.Name.Length == 0 || item.Category.Length == 0)
            return new(false, "Nom et catégorie obligatoires.");
        if (item.Stock < 0 || item.AlertThreshold < 0)
            return new(false, "Les quantités ne peuvent pas être négatives.");

        await _gate.WaitAsync();
        try
        {
            ode.Fleet(); // parc créé depuis la feuille GDM au premier usage
            var items = db.Docs<MaterielItem>(RemiseTables.Items);
            var existing = string.IsNullOrWhiteSpace(item.Id) ? null : items.FirstOrDefault(i => i.Id == item.Id);
            if (items.Any(i => i.Id != item.Id && string.Equals(i.Name, item.Name, StringComparison.OrdinalIgnoreCase)))
                return new(false, "Cette référence existe déjà.");
            var now = DateTimeOffset.UtcNow;
            if (existing is null)
            {
                item.Id = "mat-" + Guid.NewGuid().ToString("N")[..10];
                item.UpdatedAt = now;
                db.SaveDoc(RemiseTables.Items, item.Id, now, item);
                Journal(user, RemiseJournal.Materiel, item.Name, $"Création ({item.Category}), stock {item.Stock}, seuil {item.AlertThreshold}");
            }
            else
            {
                // Le stock ne change que par des mouvements (traçabilité) : on garde celui de la base.
                var changes = new List<string>();
                if (existing.Name != item.Name) changes.Add($"nom : {existing.Name} → {item.Name}");
                if (existing.Category != item.Category) changes.Add($"catégorie : {existing.Category} → {item.Category}");
                if (existing.Unit != item.Unit) changes.Add($"unité : {existing.Unit} → {item.Unit}");
                if (existing.AlertThreshold != item.AlertThreshold) changes.Add($"seuil : {existing.AlertThreshold} → {item.AlertThreshold}");
                if (existing.Active != item.Active) changes.Add(item.Active ? "réactivée" : "désactivée");
                item.Stock = existing.Stock;
                item.UpdatedAt = now;
                db.SaveDoc(RemiseTables.Items, item.Id, now, item);
                if (changes.Count > 0)
                    Journal(user, RemiseJournal.Materiel, item.Name, string.Join(" ; ", changes));
            }
            await NotifyAsync();
            return new(true, null, item.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ===== Vérifications =====

    public async Task<OdeResult> VerifyAsync(ArgosUserDto user, VerificationRequest request)
    {
        await _gate.WaitAsync();
        try
        {
            ode.Fleet(); // parc créé depuis la feuille GDM au premier usage
            var engin = db.GetDoc<FleetEngin>(RemiseTables.Engins, request.Engin ?? string.Empty);
            if (engin is null)
                return new(false, "Choisis un engin.");
            if (engin.Dotation.Count == 0)
                return new(false, "Cet engin n'a pas de dotation définie.");
            var items = db.Docs<MaterielItem>(RemiseTables.Items).ToDictionary(i => i.Id);
            var now = DateTimeOffset.UtcNow;

            var verification = new Verification
            {
                Id = Guid.NewGuid().ToString("N"),
                Engin = engin.Nom,
                At = now,
                By = user.DisplayName,
                ByUserId = user.UserId,
                Observations = (request.Observations ?? string.Empty).Trim()
            };
            foreach (var line in engin.Dotation)
            {
                var input = request.Lines.FirstOrDefault(l => l.ItemId == line.ItemId);
                var result = input is not null && CheckResults.All.Contains(input.Result) ? input.Result : CheckResults.Present;
                var counted = result == CheckResults.Absent ? 0 : Math.Max(0, input?.Counted ?? line.Required);
                verification.Lines.Add(new VerificationLine
                {
                    ItemId = line.ItemId,
                    Name = items.TryGetValue(line.ItemId, out var it) ? it.Name : line.ItemId,
                    Required = line.Required,
                    Counted = counted,
                    Result = result,
                    Observation = (input?.Observation ?? string.Empty).Trim()
                });
                line.Present = counted;
            }

            db.SaveDoc(RemiseTables.Verifications, verification.Id, now, verification);
            engin.UpdatedAt = now;
            engin.UpdatedBy = user.DisplayName;
            db.SaveDoc(RemiseTables.Engins, engin.Nom, now, engin);

            // Matériel absent ou défectueux : une anomalie est ouverte automatiquement.
            var problems = verification.Lines
                .Where(l => l.Result != CheckResults.Present || l.Counted < l.Required)
                .Select(l => l.Result == CheckResults.Present
                    ? $"{l.Name} : {l.Counted}/{l.Required}"
                    : $"{l.Name} : {l.Result.ToLowerInvariant()}{(l.Observation.Length > 0 ? " (" + l.Observation + ")" : string.Empty)}")
                .ToList();
            if (problems.Count > 0)
            {
                var record = NewMaintenance(user, engin.Nom, "Vérification : " + string.Join(" ; ", problems),
                    Immobilisations.Aucune, "Vérification", now);
                db.SaveDoc(RemiseTables.Maintenances, record.Id, now, record);
                Journal(user, RemiseJournal.Anomalie, engin.Nom, $"{record.Number} - {record.Description}");
            }

            Journal(user, RemiseJournal.VerificationFaite, engin.Nom,
                problems.Count == 0 ? "Conforme." : $"{problems.Count} écart(s) : {string.Join(" ; ", problems)}");
            await NotifyAsync();
            return new(true, null, verification.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ===== Maintenance =====

    public async Task<OdeResult> ReportAnomalyAsync(ArgosUserDto user, AnomalyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
            return new(false, "Décris le problème.");
        if (!Immobilisations.All.Contains(request.Immobilisation))
            return new(false, "Niveau d'immobilisation inconnu.");

        await _gate.WaitAsync();
        try
        {
            ode.Fleet(); // parc créé depuis la feuille GDM au premier usage
            var engin = db.GetDoc<FleetEngin>(RemiseTables.Engins, request.Engin ?? string.Empty);
            if (engin is null)
                return new(false, "Choisis l'engin concerné.");
            var now = DateTimeOffset.UtcNow;
            var record = NewMaintenance(user, engin.Nom, request.Description.Trim(), request.Immobilisation, "Signalement", now);
            db.SaveDoc(RemiseTables.Maintenances, record.Id, now, record);
            Journal(user, RemiseJournal.Anomalie, engin.Nom, $"{record.Number} ({record.Immobilisation}) - {record.Description}");

            string? warning = null;
            if (record.Immobilisation == Immobilisations.Totale && engin.Availability != FleetAvailability.Maintenance)
            {
                var applied = await ode.ApplyAvailabilityAsync(user, engin.Nom, FleetAvailability.Maintenance);
                if (applied.Ok)
                {
                    Journal(user, RemiseJournal.Disponibilite, engin.Nom, $"{engin.Availability} → {FleetAvailability.Maintenance} ({record.Number})");
                    engin.Availability = FleetAvailability.Maintenance;
                    engin.AvailabilityReason = $"{record.Number} - {Shorten(record.Description)}";
                    engin.AvailabilitySince = now;
                    engin.UpdatedAt = now;
                    engin.UpdatedBy = user.DisplayName;
                    db.SaveDoc(RemiseTables.Engins, engin.Nom, now, engin);
                }
                else
                {
                    warning = "Anomalie enregistrée. " + applied.Error;
                }
            }
            await NotifyAsync();
            return new(warning is null, warning, record.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OdeResult> AddRepairAsync(ArgosUserDto user, string id, RepairRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return new(false, "Décris la réparation.");
        await _gate.WaitAsync();
        try
        {
            ode.Fleet(); // parc créé depuis la feuille GDM au premier usage
            var record = db.GetDoc<MaintenanceRecord>(RemiseTables.Maintenances, id);
            if (record is null)
                return new(false, "Maintenance introuvable.");
            if (!record.IsOpen)
                return new(false, "Cette maintenance est clôturée.");
            var now = DateTimeOffset.UtcNow;
            record.Repairs.Add(new RepairEntry { At = now, By = user.DisplayName, Text = request.Text.Trim() });
            db.SaveDoc(RemiseTables.Maintenances, record.Id, record.ReportedAt, record);
            SetNextRevision(user, record.Engin, request.NextRevision, now);
            Journal(user, RemiseJournal.Reparation, record.Engin, $"{record.Number} - {request.Text.Trim()}");
            await NotifyAsync();
            return new(true, null, record.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OdeResult> CloseMaintenanceAsync(ArgosUserDto user, string id, CloseMaintenanceRequest request)
    {
        if (!CanManage(user))
            return new(false, "Habilitation insuffisante pour clôturer une maintenance.");
        await _gate.WaitAsync();
        try
        {
            ode.Fleet(); // parc créé depuis la feuille GDM au premier usage
            var record = db.GetDoc<MaintenanceRecord>(RemiseTables.Maintenances, id);
            if (record is null)
                return new(false, "Maintenance introuvable.");
            if (!record.IsOpen)
                return new(false, "Cette maintenance est déjà clôturée.");
            var now = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(request.Text))
                record.Repairs.Add(new RepairEntry { At = now, By = user.DisplayName, Text = request.Text.Trim() });
            record.Status = MaintenanceStatuses.Cloturee;
            record.ClosedAt = now;
            record.ClosedBy = user.DisplayName;
            db.SaveDoc(RemiseTables.Maintenances, record.Id, record.ReportedAt, record);
            SetNextRevision(user, record.Engin, request.NextRevision, now);
            Journal(user, RemiseJournal.MaintenanceCloturee, record.Engin, $"{record.Number}{Reason(request.Text)}");

            // Remise en service : seulement si plus aucune immobilisation totale n'est ouverte sur l'engin.
            string? warning = null;
            var engin = db.GetDoc<FleetEngin>(RemiseTables.Engins, record.Engin);
            if (request.ReturnToService && engin is not null && engin.Availability != FleetAvailability.Disponible)
            {
                var stillBlocked = db.Docs<MaintenanceRecord>(RemiseTables.Maintenances)
                    .Any(m => m.IsOpen && Same(m.Engin, engin.Nom) && m.Immobilisation == Immobilisations.Totale);
                if (stillBlocked)
                {
                    warning = "Maintenance clôturée. L'engin reste immobilisé : une autre anomalie totale est ouverte.";
                }
                else
                {
                    var applied = await ode.ApplyAvailabilityAsync(user, engin.Nom, FleetAvailability.Disponible);
                    if (applied.Ok)
                    {
                        Journal(user, RemiseJournal.Disponibilite, engin.Nom, $"{engin.Availability} → {FleetAvailability.Disponible} (remise en service)");
                        engin.Availability = FleetAvailability.Disponible;
                        engin.AvailabilityReason = string.Empty;
                        engin.AvailabilitySince = now;
                        engin.UpdatedAt = now;
                        engin.UpdatedBy = user.DisplayName;
                        db.SaveDoc(RemiseTables.Engins, engin.Nom, now, engin);
                    }
                }
            }
            await NotifyAsync();
            return new(warning is null, warning, record.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ===== Stocks =====

    public async Task<OdeResult> MoveStockAsync(ArgosUserDto user, StockRequest request)
    {
        if (!CanManage(user))
            return new(false, "Habilitation insuffisante pour les mouvements de stock.");
        if (!StockKinds.All.Contains(request.Kind))
            return new(false, "Type de mouvement inconnu.");
        if (request.Quantity < 0 || (request.Quantity == 0 && request.Kind != StockKinds.Ajustement))
            return new(false, "Indique une quantité positive.");

        await _gate.WaitAsync();
        try
        {
            ode.Fleet(); // parc créé depuis la feuille GDM au premier usage
            var item = db.GetDoc<MaterielItem>(RemiseTables.Items, request.ItemId ?? string.Empty);
            if (item is null)
                return new(false, "Choisis le matériel.");
            FleetEngin? engin = null;
            if (StockKinds.NeedsEngin(request.Kind))
            {
                engin = db.GetDoc<FleetEngin>(RemiseTables.Engins, request.Engin ?? string.Empty);
                if (engin is null)
                    return new(false, "Choisis l'engin.");
            }

            var q = request.Quantity;
            switch (request.Kind)
            {
                case StockKinds.Entree:
                    item.Stock += q;
                    break;
                case StockKinds.Sortie:
                    if (item.Stock < q) return new(false, $"Stock insuffisant ({item.Stock} {item.Unit}).");
                    item.Stock -= q;
                    break;
                case StockKinds.Affectation:
                {
                    if (item.Stock < q) return new(false, $"Stock insuffisant ({item.Stock} {item.Unit}).");
                    item.Stock -= q;
                    var line = engin!.Dotation.FirstOrDefault(d => d.ItemId == item.Id);
                    if (line is null) engin.Dotation.Add(line = new DotationLine { ItemId = item.Id, Required = 0 });
                    line.Present += q;
                    break;
                }
                case StockKinds.Retrait:
                {
                    var line = engin!.Dotation.FirstOrDefault(d => d.ItemId == item.Id);
                    if (line is null || line.Present < q)
                        return new(false, $"{engin.Nom} n'a que {line?.Present ?? 0} {item.Unit} de ce matériel.");
                    line.Present -= q;
                    item.Stock += q;
                    break;
                }
                case StockKinds.Ajustement:
                    item.Stock = q;
                    break;
            }

            var now = DateTimeOffset.UtcNow;
            item.UpdatedAt = now;
            db.SaveDoc(RemiseTables.Items, item.Id, now, item);
            if (engin is not null)
            {
                engin.UpdatedAt = now;
                engin.UpdatedBy = user.DisplayName;
                db.SaveDoc(RemiseTables.Engins, engin.Nom, now, engin);
            }
            var movement = new StockMovement
            {
                Id = Guid.NewGuid().ToString("N"),
                At = now,
                By = user.DisplayName,
                ItemId = item.Id,
                ItemName = item.Name,
                Kind = request.Kind,
                Quantity = q,
                Engin = engin?.Nom ?? string.Empty,
                Comment = (request.Comment ?? string.Empty).Trim(),
                StockAfter = item.Stock
            };
            db.InsertDoc(RemiseTables.Movements, movement.Id, now, movement);
            Journal(user, RemiseJournal.Mouvement, item.Name,
                $"{movement.Kind} : {q} {item.Unit}{(engin is null ? string.Empty : " - " + engin.Nom)} (stock {item.Stock}){Reason(movement.Comment)}");
            await NotifyAsync();
            return new(true, null, movement.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ===== Outils =====

    private MaintenanceRecord NewMaintenance(ArgosUserDto user, string engin, string description, string immobilisation,
        string source, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Number = $"MNT-{db.NextCounter("remise-maintenance"):0000}",
            Engin = engin,
            ReportedAt = now,
            ReportedBy = user.DisplayName,
            Description = description,
            Immobilisation = immobilisation,
            Source = source
        };

    private void SetNextRevision(ArgosUserDto user, string nom, DateTimeOffset? next, DateTimeOffset now)
    {
        if (next is null) return;
        var engin = db.GetDoc<FleetEngin>(RemiseTables.Engins, nom);
        if (engin is null) return;
        var before = engin.NextRevision;
        engin.NextRevision = next;
        engin.UpdatedAt = now;
        engin.UpdatedBy = user.DisplayName;
        db.SaveDoc(RemiseTables.Engins, engin.Nom, now, engin);
        Journal(user, RemiseJournal.EnginModifie, engin.Nom, $"prochaine révision : {Day(before)} → {Day(next)}");
    }

    private static List<string> Diff(FleetEngin a, FleetEngin b)
    {
        var changes = new List<string>();
        void Check(string label, string? x, string? y)
        {
            if (!string.Equals(x ?? string.Empty, y ?? string.Empty, StringComparison.Ordinal))
                changes.Add($"{label} : {Or(x)} → {Or(y)}");
        }
        Check("nom", a.Nom, b.Nom);
        Check("type", a.Type, b.Type);
        Check("type GDM", a.TypeGdm, b.TypeGdm);
        Check("désignation", a.Libelle, b.Libelle);
        Check("affectation", a.Centre, b.Centre);
        Check("code", a.Code, b.Code);
        Check("immatriculation", a.Immatriculation, b.Immatriculation);
        Check("prochaine révision", Day(a.NextRevision), Day(b.NextRevision));
        Check("disponibilité", a.Availability, b.Availability);
        if (a.ImageData != b.ImageData) changes.Add("photo modifiée");
        if (a.Active != b.Active) changes.Add(b.Active ? "remis au parc" : "retiré du parc");
        var dotA = string.Join(",", a.Dotation.OrderBy(d => d.ItemId).Select(d => $"{d.ItemId}:{d.Required}"));
        var dotB = string.Join(",", b.Dotation.OrderBy(d => d.ItemId).Select(d => $"{d.ItemId}:{d.Required}"));
        if (dotA != dotB) changes.Add("dotation matérielle modifiée");
        return changes;
    }

    private void Journal(ArgosUserDto user, string action, string target, string details)
        => db.AddJournal(new JournalEntry
        {
            At = DateTimeOffset.UtcNow,
            UserId = user.UserId,
            UserName = user.DisplayName,
            Action = action,
            Target = target,
            Details = details
        });

    // ===== Armement par la feuille de garde (SIGTAO) =====

    private const string NonArme = "Non armé";

    /// <summary>
    /// Arme les engins d'une feuille de garde validée : l'équipage est inscrit sur l'engin ; un engin non armé passe
    /// indisponible (« Non armé - garde du … ») et redevient disponible quand une garde suivante l'arme.
    /// Un engin engagé dans l'ODE garde sa disponibilité : le message le signale.
    /// </summary>
    public async Task<OdeResult> ArmFromGardeAsync(ArgosUserDto user, FeuilleGarde feuille)
    {
        var warnings = new List<string>();
        await _gate.WaitAsync();
        try
        {
            var fleet = ode.Fleet();
            var now = DateTimeOffset.UtcNow;
            var day = DateTime.TryParseExact(feuille.Date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d)
                ? d.ToString("dd/MM") : feuille.Date;
            foreach (var g in feuille.Engins)
            {
                var e = fleet.FirstOrDefault(f => Same(f.Nom, g.Engin));
                if (e is null) continue;
                e.Equipage = g.Arme ? g.Postes.Where(p => p.Pourvu).ToList() : [];
                e.EquipageGarde = feuille.Date;
                var reason = $"{NonArme} - garde du {day}";
                string? target = null;
                if (!g.Arme && e.Availability == FleetAvailability.Disponible) target = FleetAvailability.Indisponible;
                else if (g.Arme && e.Availability == FleetAvailability.Indisponible && e.AvailabilityReason.StartsWith(NonArme, StringComparison.Ordinal))
                    target = FleetAvailability.Disponible;
                if (target is not null)
                {
                    var applied = await ode.ApplyAvailabilityAsync(user, e.Nom, target);
                    if (applied.Ok)
                    {
                        Journal(user, RemiseJournal.Disponibilite, e.Nom, $"{e.Availability} → {target} (feuille de garde du {day})");
                        e.Availability = target;
                        e.AvailabilityReason = target == FleetAvailability.Disponible ? string.Empty : reason;
                        e.AvailabilitySince = now;
                    }
                    else warnings.Add($"{e.Nom} : {applied.Error}");
                }
                else if (!g.Arme && e.Availability == FleetAvailability.Indisponible && e.AvailabilityReason.StartsWith(NonArme, StringComparison.Ordinal))
                    e.AvailabilityReason = reason;
                e.UpdatedAt = now;
                e.UpdatedBy = user.DisplayName;
                db.SaveDoc(RemiseTables.Engins, e.Nom, now, e);
            }
            await NotifyAsync();
            return warnings.Count == 0 ? new(true, null) : new(true, string.Join(" ; ", warnings));
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task NotifyAsync() => hub.BroadcastAsync(new RealtimeMessage(RealtimeTypes.RemiseChanged));

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string Or(string? v) => string.IsNullOrWhiteSpace(v) ? "—" : v;
    private static string Day(DateTimeOffset? d) => d is null ? "—" : d.Value.ToString("dd/MM/yyyy");
    private static string Reason(string? text) => string.IsNullOrWhiteSpace(text) ? string.Empty : " - " + text.Trim();
    private static string Shorten(string v) => v.Length <= 60 ? v : v[..57] + "…";
}
