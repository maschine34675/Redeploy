using Redeploy.Server.Scav;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Xunit;

namespace Redeploy.Server.Tests;

public sealed class ScavTransferPolicyTests
{
    private static Item Root() => new()
    {
        Id = new MongoId(ScavTransferPolicy.TransferRootId),
        Template = new MongoId(ScavTransferPolicy.TransferRootTemplate)
    };

    [Fact] public void NoTransfersAndEmptyArraysAreAccepted()
    {
        Assert.True(ScavTransferPolicy.ContainsNoTransferredItems(null));
        Assert.True(ScavTransferPolicy.ContainsNoTransferredItems([]));
        Assert.True(ScavTransferPolicy.ContainsNoTransferredItems(new() { ["unused"] = [] }));
    }

    [Theory] [InlineData("_btr")] [InlineData("_transit")]
    public void EmptyNativeTransferRootIsAccepted(string suffix)
        => Assert.True(ScavTransferPolicy.ContainsNoTransferredItems(new() { [ScavTransferPolicy.TransferRootId + suffix] = [Root()] }));

    [Fact] public void BothEmptyNativeTransferRootsAreAccepted()
        => Assert.True(ScavTransferPolicy.ContainsNoTransferredItems(new()
        {
            [ScavTransferPolicy.TransferRootId + "_btr"] = [Root()],
            [ScavTransferPolicy.TransferRootId + "_transit"] = [Root()]
        }));

    [Fact] public void RealContentsAndDuplicateRootsAreRejected()
    {
        var child = new Item { Id = new MongoId(), Template = new MongoId(), ParentId = ScavTransferPolicy.TransferRootId, SlotId = "main" };
        Assert.False(ScavTransferPolicy.ContainsNoTransferredItems(new() { [ScavTransferPolicy.TransferRootId + "_btr"] = [Root(), child] }));
        Assert.False(ScavTransferPolicy.ContainsNoTransferredItems(new() { [ScavTransferPolicy.TransferRootId + "_btr"] = [Root(), Root()] }));
    }

    [Theory] [InlineData("id")] [InlineData("template")] [InlineData("parent")]
    [InlineData("slot")] [InlineData("location")] [InlineData("key")]
    public void MalformedLoneItemCannotPretendToBeEmptyTransfer(string mutation)
    {
        var root = Root(); string key = ScavTransferPolicy.TransferRootId + "_btr";
        switch (mutation)
        {
            case "id": root.Id = new MongoId(); break;
            case "template": root.Template = new MongoId(); break;
            case "parent": root.ParentId = new MongoId().ToString(); break;
            case "slot": root.SlotId = "main"; break;
            case "location": root.Location = 0; break;
            case "key": key = ScavTransferPolicy.TransferRootId + "_unexpected"; break;
        }
        Assert.False(ScavTransferPolicy.ContainsNoTransferredItems(new() { [key] = [root] }));
    }

    [Fact] public void NullArrayOrNullRootIsRejected()
    {
        Assert.False(ScavTransferPolicy.ContainsNoTransferredItems(new() { ["unused"] = null! }));
        Assert.False(ScavTransferPolicy.ContainsNoTransferredItems(new() { [ScavTransferPolicy.TransferRootId + "_btr"] = [null!] }));
    }
}
