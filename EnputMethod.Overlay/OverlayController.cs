using System.Windows;
using System.Windows.Threading;
using System.Collections.Generic;

namespace EnputMethod.Overlay;

internal sealed class OverlayController
{
    private readonly Dictionary<string, CandidateOverlayWindow> _candidateWindows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TranslationOverlayWindow> _translationWindows = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _foregroundTimer;
    private CandidateOverlayWindow? _warmCandidateWindow;

    public OverlayController()
    {
        _foregroundTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(75) };
        _foregroundTimer.Tick += (_, _) => RefreshForegroundVisibility();
        _foregroundTimer.Start();
    }


    internal void WarmUp()
    {
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            if (_warmCandidateWindow is not null) return;
            _warmCandidateWindow = new CandidateOverlayWindow();
            _warmCandidateWindow.WarmUp();
            OverlayDiagnostics.Write("candidate.warmed");
        }));
    }
    public void HandleHostMessage(OverlayMessage message, Func<OverlayMessage, Task> sendAction)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            switch (message.Type)
            {
                case "showCandidates" when message.Candidates is not null:
                    CandidateWindowFor(message.ClientId).ShowCandidates(message.ClientId, message.StateId, message.Candidates, sendAction);
                    OverlayDiagnostics.Write("candidate.presented", $"state={message.StateId} client={message.ClientId}");
                    break;
                case "showTranslation" when message.Translation is not null:
                    CandidateOverlayWindow? candidateWindow = _candidateWindows.GetValueOrDefault(message.ClientId);
                    TranslationWindowFor(message.ClientId).ShowTranslation(message.ClientId, message.Translation,
                        candidateWindow?.ScreenBoundsFor(message.ClientId));
                    break;
                case "hide":
                    if (message.Surface is not "translation") HideCandidateWindow(message.ClientId);
                    if (message.Surface is not "candidates") HideTranslationWindow(message.ClientId);
                    break;
            }
            RefreshForegroundVisibility();
        });
    }
    public void HandleClientDisconnected(string clientId)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            CloseCandidateWindow(clientId);
            CloseTranslationWindow(clientId);
        });
    }

    private CandidateOverlayWindow CandidateWindowFor(string clientId)
    {
        if (_candidateWindows.TryGetValue(clientId, out CandidateOverlayWindow? window)) return window;
        window = _warmCandidateWindow ?? new CandidateOverlayWindow();
        _warmCandidateWindow = null;
        _candidateWindows.Add(clientId, window);
        return window;
    }

    private TranslationOverlayWindow TranslationWindowFor(string clientId)
    {
        if (_translationWindows.TryGetValue(clientId, out TranslationOverlayWindow? window)) return window;
        window = new TranslationOverlayWindow();
        _translationWindows.Add(clientId, window);
        return window;
    }
    private void RefreshForegroundVisibility()
    {
        foreach (CandidateOverlayWindow window in _candidateWindows.Values) window.RefreshForegroundVisibility();
        foreach (TranslationOverlayWindow window in _translationWindows.Values) window.RefreshForegroundVisibility();
    }


    private void HideCandidateWindow(string clientId)
    {
        if (_candidateWindows.TryGetValue(clientId, out CandidateOverlayWindow? window)) window.HideFor(clientId);
    }

    private void HideTranslationWindow(string clientId)
    {
        if (_translationWindows.TryGetValue(clientId, out TranslationOverlayWindow? window)) window.HideFor(clientId);
    }

    private void CloseCandidateWindow(string clientId)
    {
        if (_candidateWindows.Remove(clientId, out CandidateOverlayWindow? window)) window.Close();
    }

    private void CloseTranslationWindow(string clientId)
    {
        if (_translationWindows.Remove(clientId, out TranslationOverlayWindow? window)) window.Close();
    }
}
