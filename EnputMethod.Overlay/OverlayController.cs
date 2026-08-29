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
                    _candidateWindow.ShowCandidates(message.ClientId, message.StateId, message.Candidates, sendAction);
                    _ = sendAction(new OverlayMessage { Type = "presented", ClientId = message.ClientId, StateId = message.StateId });
                    break;
                case "showTranslation" when message.Translation is not null:
                    _translationWindow.ShowTranslation(message.ClientId, message.Translation);
                    break;
                case "hide":
                    if (message.Surface is not "translation") _candidateWindow.HideFor(message.ClientId);
                    if (message.Surface is not "candidates") _translationWindow.HideFor(message.ClientId);
                    break;
            }
        });
    }
}
