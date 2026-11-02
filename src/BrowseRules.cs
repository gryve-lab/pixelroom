using System;
using System.Collections.Generic;
using System.Linq;

namespace Pixelroom
{
    public enum BrowseSort { NameAscending, NameDescending, Newest, Oldest, Largest, Smallest }
    public enum SharingFilter { All, Shared, NotShared, Direct, Inherited }
    public enum BrowseDate { Modified, Created, Uploaded }

    public sealed class BrowseQuery
    {
        public string Search = "";
        public SharingFilter Sharing;
        public BrowseSort Sort = BrowseSort.NameAscending;
        public BrowseDate DateField;
        public DateTime? From, To;
        public bool FoldersFirst = true;
        public bool Active { get { return Sharing != SharingFilter.All || From.HasValue || To.HasValue || !String.IsNullOrWhiteSpace(Search); } }

        public bool MatchesSharing(bool shared, bool direct)
        {
            switch (Sharing)
            {
                case SharingFilter.Shared: return shared;
                case SharingFilter.NotShared: return !shared;
                case SharingFilter.Direct: return shared && direct;
                case SharingFilter.Inherited: return shared && !direct;
                default: return true;
            }
        }
        public bool MatchesDate(DateTime date)
        {
            if (!From.HasValue && !To.HasValue) return true;
            if (date == DateTime.MinValue) return false;
            DateTime day = date.ToLocalTime().Date;
            return (!From.HasValue || day >= From.Value.Date) && (!To.HasValue || day <= To.Value.Date);
        }
        public List<FilesystemNode> Apply(IEnumerable<FilesystemNode> source)
        {
            string term = (Search ?? "").Trim();
            IEnumerable<FilesystemNode> filtered = source.Where(n => (term.Length == 0 || (n.Name ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                && MatchesSharing(n.IsShared, n.OwnLinkReadable) && MatchesDate(DateField == BrowseDate.Created ? n.Created : n.Modified));
            IOrderedEnumerable<FilesystemNode> ordered = filtered.OrderByDescending(n => FoldersFirst && n.IsDirectory);
            switch (Sort)
            {
                case BrowseSort.NameDescending: ordered = ordered.ThenByDescending(n => n.Name, StringComparer.CurrentCultureIgnoreCase); break;
                case BrowseSort.Newest: ordered = ordered.ThenByDescending(n => DateField == BrowseDate.Created ? n.Created : n.Modified); break;
                case BrowseSort.Oldest: ordered = ordered.ThenBy(n => DateField == BrowseDate.Created ? n.Created : n.Modified); break;
                case BrowseSort.Largest: ordered = ordered.ThenByDescending(n => n.Size); break;
                case BrowseSort.Smallest: ordered = ordered.ThenBy(n => n.Size); break;
                default: ordered = ordered.ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase); break;
            }
            return ordered.ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(n => n.Path, StringComparer.Ordinal).ToList();
        }
    }
}
