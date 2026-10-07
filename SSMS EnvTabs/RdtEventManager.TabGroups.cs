using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Linq;

namespace SSMS_EnvTabs
{
    internal sealed partial class RdtEventManager
    {
        /// <summary>
        /// Resolves the named group of the active query tab, using the same rule precedence as renaming.
        /// </summary>
        internal bool TryGetActiveTabGroup(out string groupName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            groupName = null;

            if (LoadConfigOrNull() == null)
            {
                return false;
            }

            if (!TryGetActiveWindowFrame(out IVsWindowFrame activeFrame)
                || !TryGetMonikerFromFrame(activeFrame, out string moniker)
                || !IsSqlDocumentMoniker(moniker))
            {
                return false;
            }

            TryGetConnectionInfo(activeFrame, out string server, out string database);
            groupName = TabRuleMatcher.ResolveGroupName(cachedRules, cachedManualRules, moniker, server, database);
            return groupName != null;
        }

        /// <summary>
        /// Closes every open query tab in the active tab's group. Modified tabs get the standard
        /// save prompt; cancelling it stops closing the remaining tabs.
        /// Returns false when the active tab is not in a named group.
        /// </summary>
        internal bool CloseTabsInActiveGroup(out string groupName, out int closedCount)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            closedCount = 0;

            if (!TryGetActiveTabGroup(out groupName))
            {
                return false;
            }

            string activeMoniker = null;
            if (TryGetActiveWindowFrame(out IVsWindowFrame activeFrame))
            {
                TryGetMonikerFromFrame(activeFrame, out activeMoniker);
            }

            string targetGroup = groupName;
            var targets = GetOpenDocumentsSnapshot()
                .Where(doc => string.Equals(
                    TabRuleMatcher.ResolveGroupName(cachedRules, cachedManualRules, doc.Moniker, doc.Server, doc.Database),
                    targetGroup,
                    StringComparison.OrdinalIgnoreCase))
                // Close the active tab last so focus does not hop through tabs that are about to close.
                .OrderBy(doc => string.Equals(doc.Moniker, activeMoniker, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var doc in targets)
            {
                int hr;
                try
                {
                    hr = doc.Frame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_PromptSave);
                }
                catch (Exception ex)
                {
                    EnvTabsLog.Info($"CloseTabsInActiveGroup: close failed cookie={doc.Cookie}: {ex.Message}");
                    continue;
                }

                if (hr == VSConstants.OLE_E_PROMPTSAVECANCELLED || hr == VSConstants.E_ABORT)
                {
                    EnvTabsLog.Info($"CloseTabsInActiveGroup: save prompt cancelled; stopping. Group='{targetGroup}', Closed={closedCount}");
                    break;
                }

                if (ErrorHandler.Succeeded(hr))
                {
                    closedCount++;
                }
            }

            EnvTabsLog.Info($"CloseTabsInActiveGroup: Group='{targetGroup}', Targets={targets.Count}, Closed={closedCount}");
            return true;
        }
    }
}
