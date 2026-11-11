using System;
using System.Collections.Generic;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Panel.Core;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CopilotUsageMeter.Panel.Vsix
{
    // Shows the fixed-wording notices as infobars at the top of the Visual Studio main window. Every member must be called on the UI thread.
    internal sealed class InfoBarPresenter : IVsInfoBarUIEvents, IDisposable
    {
        private sealed class Shown
        {
            public Shown(IVsInfoBarUIElement element, uint cookie, ExtensionNotice notice)
            {
                Element = element;
                Cookie = cookie;
                Notice = notice;
            }

            public IVsInfoBarUIElement Element { get; }
            public uint Cookie { get; }
            public ExtensionNotice Notice { get; }
        }

        private readonly IServiceProvider services;
        private readonly Action<ExtensionNotice> dismissed;
        private readonly Dictionary<ExtensionNotice, Shown> shown = new Dictionary<ExtensionNotice, Shown>();

        public InfoBarPresenter(IServiceProvider services, Action<ExtensionNotice> dismissed)
        {
            this.services = services;
            this.dismissed = dismissed;
        }

        // Returns false when Visual Studio has no infobar host yet or refuses the infobar, so the caller can fall back to the status bar.
        public bool Show(ExtensionNotice notice)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (shown.ContainsKey(notice)) return true;
                var shell = services.GetService(typeof(SVsShell)) as IVsShell;
                var factory = services.GetService(typeof(SVsInfoBarUIFactory)) as IVsInfoBarUIFactory;
                if (shell == null || factory == null)
                {
                    Diagnostics.Warning("vsix.infobar", "host_unavailable");
                    return false;
                }
                var result = shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out var hostObject);
                if (!(hostObject is IVsInfoBarHost host) || result != 0)
                {
                    Diagnostics.Warning("vsix.infobar", "host_unavailable");
                    return false;
                }
                var model = new InfoBarModel(ExtensionNoticeText.Message(notice), KnownMonikers.StatusWarning, isCloseButtonVisible: true);
                var element = factory.CreateInfoBar(model);
                if (element == null)
                {
                    Diagnostics.Warning("vsix.infobar", "create_failed");
                    return false;
                }
                element.Advise(this, out var cookie);
                shown[notice] = new Shown(element, cookie, notice);
                host.AddInfoBar(element);
                Diagnostics.Warning("vsix.infobar", "shown." + notice);
                return true;
            }
            catch (Exception exception)
            {
                Diagnostics.Error("vsix.infobar", "show_failed", exception);
                if (shown.TryGetValue(notice, out var partial))
                {
                    shown.Remove(notice);
                    try
                    {
                        partial.Element.Unadvise(partial.Cookie);
                        partial.Element.Close();
                    }
                    catch (Exception)
                    {
                    }
                }
                return false;
            }
        }

        public void Close(ExtensionNotice notice)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (!shown.TryGetValue(notice, out var entry)) return;
                shown.Remove(notice);
                entry.Element.Unadvise(entry.Cookie);
                entry.Element.Close();
            }
            catch (Exception exception)
            {
                Diagnostics.Error("vsix.infobar", "close_failed", exception);
            }
        }

        public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                foreach (var entry in new List<Shown>(shown.Values))
                {
                    if (!ReferenceEquals(entry.Element, infoBarUIElement)) continue;
                    shown.Remove(entry.Notice);
                    infoBarUIElement.Unadvise(entry.Cookie);
                    dismissed(entry.Notice);
                }
            }
            catch (Exception exception)
            {
                Diagnostics.Error("vsix.infobar", "closed_handler_failed", exception);
            }
        }

        public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
        {
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (var notice in new List<ExtensionNotice>(shown.Keys)) Close(notice);
        }
    }
}
