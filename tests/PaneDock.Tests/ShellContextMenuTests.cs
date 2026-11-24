using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.App;

[TestClass]
public sealed class ShellContextMenuTests
{
    [TestMethod]
    public void CanonicalFileVerbsUsePaneDockSafetyCommands()
    {
        Assert.AreEqual(FileCommand.Copy, ShellContextMenuService.GetPaneDockAction("copy"));
        Assert.AreEqual(FileCommand.Cut, ShellContextMenuService.GetPaneDockAction("CuT"));
        Assert.AreEqual(FileCommand.Paste, ShellContextMenuService.GetPaneDockAction("paste"));
        Assert.AreEqual(FileCommand.Rename, ShellContextMenuService.GetPaneDockAction("rename"));
        Assert.AreEqual(FileCommand.Delete, ShellContextMenuService.GetPaneDockAction("delete"));
    }

    [TestMethod]
    public void ExtensionAndUnknownVerbsAreNotReinterpretedAsFileCommands()
    {
        foreach (var verb in new string?[] { null, "", "open", "properties", "edit",
                     "compress", "git_shell", "Notepad++", "copyaspath" })
            Assert.IsNull(ShellContextMenuService.GetPaneDockAction(verb));
    }
}
