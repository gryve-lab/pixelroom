using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pixelroom;

internal static partial class Tests
{
    private static async Task CheckBrowsingAndTransfers()
    {
        await CheckLegacySharing();
        ExplorerFixture fixture = new ExplorerFixture(); fixture.Folder("/me/Public"); fixture.Folder("/me/Public/Nested"); fixture.File("/me/Public/Nested/report.txt", new byte[24]); fixture.Folder("/me/Private"); fixture.File("/me/Private/link.txt", new byte[12]);
        fixture.Nodes["/me/Public"].Id = "pub12345"; fixture.LinkPermissions["/me/Public"] = new bool[] { true, false, false };
        fixture.Nodes["/me/Private/link.txt"].Id = "file1234"; fixture.LinkPermissions["/me/Private/link.txt"] = new bool[] { true, false, false };
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        {
            FilesystemDirectory top = await client.ListFilesystemAsync("/me", ExplorerFixture.Key, CancellationToken.None);
            Assert(top.Children.Single(n => n.Name == "Public").SharingText == "Shared" && !top.Children.Single(n => n.Name == "Private").IsShared, "Folder sharing must use public read permissions.");
            FilesystemDirectory nested = await client.ListFilesystemAsync("/me/Public/Nested", ExplorerFixture.Key, CancellationToken.None);
            Assert(nested.Current.InheritedShared && nested.Children.Single().SharingText == "Shared via parent", "Sharing through distant ancestors must apply to nested folders and files.");
            Assert(fixture.Mutations.Count == 0 && fixture.Requests.Count == 2, "Sharing badges must not publish anything or scan every descendant.");
            fixture.LinkPermissions["/me/Public"] = new bool[] { false, true, false };
            nested = await client.ListFilesystemAsync("/me/Public/Nested", ExplorerFixture.Key, CancellationToken.None);
            Assert(!nested.Current.IsShared && !nested.Children.Single().IsShared, "A public ID without read permission must not be shown as a readable share.");
            FilesystemDirectory privateFolder = await client.ListFilesystemAsync("/me/Private", ExplorerFixture.Key, CancellationToken.None);
            Assert(!privateFolder.Current.IsShared && privateFolder.Children.Single().OwnLinkReadable, "An independently shared child must not make the whole parent folder shared.");
        }
        Pass("listings mark direct and inherited sharing without publishing, scanning or confusing parent access");

        DateTime first = new DateTime(2026, 10, 1, 22, 30, 0, DateTimeKind.Local), last = first.AddDays(4);
        FilesystemNode[] nodes = {
            new FilesystemNode { Name = "Zulu", Path = "/me/Zulu", Type = "dir", Modified = first, Created = last },
            new FilesystemNode { Name = "alpha.txt", Path = "/me/alpha.txt", Type = "file", Modified = last, Created = first, Size = 200, OwnLinkReadable = true },
            new FilesystemNode { Name = "Beta.txt", Path = "/me/Beta.txt", Type = "file", Modified = first.AddDays(2), Created = first, Size = 100, InheritedShared = true },
            new FilesystemNode { Name = "no-date", Path = "/me/no-date", Type = "file", Size = 5 }
        };
        BrowseQuery query = new BrowseQuery { FoldersFirst = false };
        Assert(query.Apply(nodes).First().Name == "alpha.txt", "Alphabetic sorting must ignore case.");
        query.Sort = BrowseSort.NameDescending; Assert(query.Apply(nodes).First().Name == "Zulu", "Reverse alphabetic order failed.");
        query.Sort = BrowseSort.Newest; Assert(query.Apply(nodes).First().Name == "alpha.txt", "Newest sort must use the selected date field.");
        query.DateField = BrowseDate.Created; Assert(query.Apply(nodes).First().Name == "Zulu", "Created-date sorting must not use modified dates.");
        query.DateField = BrowseDate.Modified; query.Sort = BrowseSort.Largest; Assert(query.Apply(nodes).First().Size == 200, "Largest-first sorting failed.");
        query.Sort = BrowseSort.Smallest; Assert(query.Apply(nodes).First().Size == 0, "Smallest-first sorting failed.");
        query.FoldersFirst = true; Assert(query.Apply(nodes).First().IsDirectory, "Folders-first preference must work for every sort mode.");
        Assert(nodes[0].Name == "Zulu" && nodes.Length == 4, "Filtering must not mutate the server listing.");
        Pass("alphabetic, reverse, newest, created-date and size sorting preserve the source and folders-first preference");

        query = new BrowseQuery { Sharing = SharingFilter.Shared }; Assert(query.Apply(nodes).Count == 2, "Shared filter must include direct and inherited access.");
        query.Sharing = SharingFilter.NotShared; Assert(query.Apply(nodes).Count == 2, "Private filter must exclude all shared access.");
        query.Sharing = SharingFilter.Direct; Assert(query.Apply(nodes).Single().Name == "alpha.txt", "Direct link filtering failed.");
        query.Sharing = SharingFilter.Inherited; Assert(query.Apply(nodes).Single().Name == "Beta.txt", "Parent link filtering failed.");
        query.Search = "MISSING"; Assert(query.Apply(nodes).Count == 0, "Search must combine with sharing filters.");
        Pass("sharing filters combine search with direct, inherited and private access");

        query = new BrowseQuery { From = first.Date, To = first.Date };
        Assert(query.MatchesDate(first) && !query.MatchesDate(last) && !query.MatchesDate(DateTime.MinValue), "Date range must include the entire final day and exclude unavailable dates.");
        query.To = last.Date; Assert(query.Apply(nodes).Count == 3, "Date range must preserve both endpoints.");
        query.From = last.Date; query.DateField = BrowseDate.Created; Assert(query.Apply(nodes).Single().Name == "Zulu", "Date filtering must use the selected field.");
        query.From = query.To = null; Assert(query.MatchesDate(DateTime.MinValue), "Clearing dates must restore undated items.");
        Assert(new BrowseQuery { Sharing = SharingFilter.NotShared }.MatchesSharing(true, true) == false, "My files entries are shared by link.");
        Pass("inclusive local date ranges handle created dates, missing dates and reset correctly");

        TransferMetrics metrics = new TransferMetrics(); metrics.UpdateAt(0, 10000, 0); metrics.UpdateAt(2000, 10000, 2);
        Assert(metrics.Percent == 20 && Math.Abs(metrics.SpeedAt(2) - 1000) < .01 && metrics.EtaAt(2) == "8s", "Transfer rate and ETA must derive from transferred bytes and elapsed time.");
        Assert(metrics.SpeedAt(6) == 0 && metrics.EtaAt(6) == "—", "A stalled transfer must not keep showing an old speed or ETA.");
        metrics.Finish(false); metrics.UpdateAt(9000, 10000, 7); Assert(metrics.Done == 2000 && metrics.Percent == 20, "Late progress must not change a cancelled transfer.");
        Pass("transfer rate, percentage, ETA, stalls and late cancellation callbacks are handled correctly");

        metrics.Reset(); metrics.UpdateAt(1234, -1, 2); Assert(!metrics.HasLength && metrics.Percent == 0 && metrics.EtaAt(2) == "—", "Unknown-length downloads must not invent a percentage or ETA.");
        metrics.Finish(false); Assert(metrics.Percent == 0, "A cancelled unknown-length transfer must not appear complete.");
        metrics.Reset(); metrics.UpdateAt(0, 0, 0); metrics.Finish(true); Assert(metrics.Percent == 100 && metrics.Done == 0 && metrics.Total == 0, "Zero-byte file completion failed.");
        metrics.Reset(); metrics.UpdateAt(5368709120L, 10737418240L, 10); Assert(metrics.Percent == 50 && metrics.BytesText.Contains("GB"), "Large transfers must retain 64-bit byte counters.");
        Pass("transfer metrics handle unknown lengths, empty files and transfers over 4 GB");
    }
    private static async Task CheckLegacySharing()
    {
        ExplorerFixture fixture = new ExplorerFixture();
        fixture.Folder("/me/Web"); fixture.Folder("/me/Web/Nested"); fixture.File("/me/Web/Nested/report.txt", new byte[1]);
        fixture.File("/me/old.txt", new byte[1]); fixture.File("/me/private.txt", new byte[1]); fixture.File("/me/blocked.txt", new byte[1]); fixture.File("/me/null.txt", new byte[1]);
        fixture.Nodes["/me/Web"].Id = "oldweb12"; fixture.Nodes["/me/old.txt"].Id = "oldfile1"; fixture.Nodes["/me/blocked.txt"].Id = "blocked1"; fixture.Nodes["/me/null.txt"].Id = "null1234";
        fixture.OmitLinkPermissions.UnionWith(new string[] { "/me", "/me/Web", "/me/old.txt", "/me/private.txt" }); fixture.Nodes["/me"].Id = "me"; fixture.NullLinkPermissions.Add("/me/null.txt");
        fixture.LinkPermissions["/me/blocked.txt"] = new bool[] { false, true, false };
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        {
            FilesystemDirectory root = await client.ListFilesystemAsync("/me", ExplorerFixture.Key, CancellationToken.None);
            Assert(!root.Current.IsShared && root.Children.Single(n => n.Name == "Web").OwnLinkReadable && root.Children.Single(n => n.Name == "old.txt").OwnLinkReadable && root.Children.Single(n => n.Name == "null.txt").OwnLinkReadable, "Public IDs with omitted permissions must use PixelDrain's read-only default; /me must remain private.");
            Assert(!root.Children.Single(n => n.Name == "private.txt").IsShared && !root.Children.Single(n => n.Name == "blocked.txt").IsShared, "A missing public ID or explicit read=false must never become readable through the default.");
            Pass("website shares with omitted permissions default to read-only while private and restricted items stay private");

            FilesystemDirectory nested = await client.ListFilesystemAsync("/me/Web/Nested", ExplorerFixture.Key, CancellationToken.None);
            Assert(nested.Current.SharingText == "Shared via parent" && nested.Children.Single().InheritedShared, "Legacy ancestor links must reach nested children.");
            Assert(new BrowseQuery { Sharing = SharingFilter.Shared }.Apply(root.Children).Count == 3 && new BrowseQuery { Sharing = SharingFilter.NotShared }.Apply(root.Children).Count == 2, "Shared and Not shared filters must use the same legacy link rules as the badges.");
            Pass("legacy sharing propagates through ancestors and matches sharing filters without extra requests");

            FilesystemShareInfo direct = await client.GetFilesystemShareAsync("/me/old.txt", ExplorerFixture.Key, CancellationToken.None);
            FilesystemShareInfo parent = await client.GetFilesystemShareAsync("/me/Web/Nested/report.txt", ExplorerFixture.Key, CancellationToken.None);
            Assert(direct.OwnShared && direct.LinkRead && !direct.LinkWrite && !direct.LinkDelete && direct.ShareUrl == "https://pixeldrain.com/d/oldfile1", "Existing web share links must copy without being recreated or gaining write permissions.");
            Assert(parent.ParentShared && !parent.OwnShared && parent.LinkRead && parent.ShareUrl == "https://pixeldrain.com/d/oldweb12/Nested/report.txt", "Inherited legacy links must use the public ancestor path.");
            Assert(fixture.Mutations.Count == 0 && fixture.Requests.Count == 4, "Detecting old shares must only read the requested paths.");
            Pass("old direct and inherited web links are available without publishing or changing permissions");

            fixture.LinkPermissions["/me/Web"] = new bool[] { false, false, false }; fixture.OmitLinkPermissions.Remove("/me/Web");
            nested = await client.ListFilesystemAsync("/me/Web/Nested", ExplorerFixture.Key, CancellationToken.None);
            parent = await client.GetFilesystemShareAsync("/me/Web/Nested/report.txt", ExplorerFixture.Key, CancellationToken.None);
            Assert(!nested.Current.IsShared && !nested.Children.Single().IsShared && !parent.LinkRead && parent.ShareUrl == "", "Refreshing after an explicit restriction must remove old inherited read links.");
            Assert(fixture.Mutations.Count == 0, "Refresh must never repair links by changing server permissions.");
            Pass("refresh respects explicit restrictions on previously readable legacy shares");
        }
    }
}
