using Avalonia;
using Avalonia.Controls;

namespace Argos.Views.Mobile.Ilias;

/// <summary>Questionnaire ILIAS en version iPhone : chronomètre, questions, résultat et corrections.</summary>
public partial class QuizMobileView : UserControl
{
    public QuizMobileView() => InitializeComponent();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
