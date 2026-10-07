using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using System.Windows.Forms;
using Task = System.Threading.Tasks.Task;

namespace SSMS_EnvTabs
{
    internal sealed class CloseTabGroupCommand
    {
        public const int DocTabContextCommandId = 0x0107;
        public const int ToolbarCommandId = 0x0108;
        public static readonly Guid CommandSet = SSMS_EnvTabsPackage.PackageCmdSetGuid;

        private const string DefaultContextMenuText = "Close All Tabs in Group";

        private readonly SSMS_EnvTabsPackage package;

        private CloseTabGroupCommand(SSMS_EnvTabsPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            // Document tab right-click menu: shows the group name and is disabled when the tab has no group.
            var docTabCommand = new OleMenuCommand(this.Execute, new CommandID(CommandSet, DocTabContextCommandId));
            docTabCommand.BeforeQueryStatus += this.OnDocTabBeforeQueryStatus;
            commandService.AddCommand(docTabCommand);

            // Toolbar button: fixed text, explains itself when the active tab has no group.
            commandService.AddCommand(new MenuCommand(this.Execute, new CommandID(CommandSet, ToolbarCommandId)));
        }

        public static async Task InitializeAsync(SSMS_EnvTabsPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            new CloseTabGroupCommand(package, commandService);
        }

        private void OnDocTabBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(sender is OleMenuCommand command))
            {
                return;
            }

            string groupName = null;
            bool hasGroup = false;
            try
            {
                hasGroup = package.RdtEventManagerInstance?.TryGetActiveTabGroup(out groupName) == true;
            }
            catch (Exception ex)
            {
                EnvTabsLog.Info($"CloseTabGroupCommand query status failed: {ex.Message}");
            }

            command.Enabled = hasGroup;
            // '&' is a mnemonic marker in menu text; double it so group names display literally.
            command.Text = hasGroup
                ? $"Close All Tabs in '{groupName.Replace("&", "&&")}'"
                : DefaultContextMenuText;
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (package.RdtEventManagerInstance == null)
                {
                    MessageBox.Show(
                        "EnvTabs is not fully initialized yet. Please try again.",
                        "SSMS EnvTabs",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                bool handled = package.RdtEventManagerInstance.CloseTabsInActiveGroup(out _, out _);
                if (!handled)
                {
                    MessageBox.Show(
                        "The active tab does not belong to a named connection group.",
                        "SSMS EnvTabs",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                EnvTabsLog.Info($"CloseTabGroupCommand failed: {ex.Message}");
            }
        }
    }
}
