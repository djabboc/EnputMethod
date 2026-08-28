using System.Windows;

namespace EnputMethod.Overlay;

internal sealed class OverlayController
{
    private readonly CandidateOverlayWindow _candidateWindow = new();
    private readonly TranslationOverlayWindow _translationWindow = new();

    public void HandleHostMessage(OverlayMessage message, Func<OverlayMessage, Task> sendAction)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            switch (message.Type)
            {
                case "showCandidates" when message.Candidates is not null:
                    _candidateWindow.ShowCandidates(message.StateId, message.Candidates, sendAction);
                    break;
                case "showTranslation" when message.Translation is not null:
                    _translationWindow.ShowTranslation(message.Translation);
                    break;
                case "hide":
                    _candidateWindow.Hide();
                    _translationWindow.Hide();
                    break;
            }
        });
    }
}
