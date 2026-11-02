PIXELROOM 1.8

1. Extract the ZIP and open Pixelroom.exe.
2. Open Settings, enter your PixelDrain API key, and select Connect.
3. Choose Gray or Dark under Appearance. Your theme is saved automatically.
4. Use Upload > Upload files to select files, or drag them into the window.
5. Select files and choose Download. Multiple files are saved as a ZIP.
6. Right-click files for sharing, favorites, collections, details, and deletion.
7. Open File System for real PixelDrain folders. Double-click a folder to open it.
   Use Root, Up, or enter a path starting with /me to navigate.
8. File System supports uploads, file downloads, folder ZIP downloads, new folders,
   rename, move, delete, and importing file IDs from My files.
9. Select files or folders in File System and choose Copy (Ctrl+C), open the
   destination folder, then Paste (Ctrl+V). Right-click menus also provide Copy
   and Paste. Existing names get a " - Copy" suffix instead of being replaced.
   Copies include subfolders and empty folders. They use your computer's connection
   to read and upload the contents. Cancelled or failed copies can leave completed
   items in the destination; the original files are kept.
10. Search this folder filters files and folders in the current File System folder.
    Press Ctrl+F to search; Clear or Escape restores the full listing. My files,
    Favorites, Recent, and Collections also support searching their current view.
11. Drag files or folders from Windows Explorer onto My files, Transfers, or the
    File System file list, including an empty folder area. File System uploads go
    to the folder currently open. Dropping folders on My files or Transfers opens
    File System and uploads the folders into /me. Dragging cloud files out to
    Windows Explorer is not supported.
12. In File System, choose Upload folders. Hold Ctrl or Shift to select several
    folders in the native Windows picker, then choose Select folders. You can also
    use Upload > Upload folders to File System from the main window.
    Subfolders, empty folders and zero-byte files are preserved. Existing folders
    are kept and new uploads receive names such as Folder (2). Replacing existing
    top-level files requires approval. Linked files and folders are not followed.
    Up to 50,000 items can be uploaded in one batch. A cancelled or failed upload
    can leave completed items in the destination. Local source files are kept.
13. Select files in My files and choose Share to copy share links or direct
    hotlinks. The right-click menu also offers Copy hotlink.
14. Select a file or folder in File System and choose Share. Create read-only link
    allows anyone with the link to view or download that item; folder links include
    its contents. Copy the share link or hotlink. Folder hotlinks download a ZIP.
    Stop sharing disables the item's own link. Access through a shared parent
    folder remains until sharing is disabled on that parent.
    Managing sharing requires the appropriate API key permissions. Hotlinks follow
    your PixelDrain account settings and transfer limits. No API key is included
    in copied links. See https://docs.pixeldrain.com/filesystem/ for account rules.
15. All sidebar pages, including File System and Settings, open inside the main
    window. Returning to File System keeps the last folder, search and filters.
    Folder uploads continue when you switch sidebar pages.
16. The Sharing column and cyan share badge show active read links. Shared via
    parent means a shared ancestor provides access to the item. A folder marked
    Not shared can still contain individual items with their own shared links.
    My files entries are always shared by their file links. Opening a listing or
    filtering it never creates, changes or removes sharing permissions.
    Public IDs created on the website are recognized even when link_permissions
    is omitted: PixelDrain defaults those links to read-only. Explicit access
    restrictions are respected. Select Refresh or F5 to load changes from the web.
17. Use the Sharing filter for Shared, Not shared, Direct link or Shared via parent.
    Sort File System by name, newest, oldest or size. Folders first is optional.
    Click the Name, Size or date column header to change its sort direction.
18. Filter dates with Today, Last 7 days, Last 30 days, or Custom range. Both ends
    of a custom range are inclusive and use your local calendar dates. File System
    can use Modified date or Created date; other file lists use Uploaded date.
    Select the custom range again to edit it. Reset filters restores all items.
19. Transfers shows the active file, direction, status, progress bar, transferred
    bytes, speed and estimated time remaining. File System uploads, downloads and
    copies also appear here. Unknown download sizes are shown without a guessed
    percentage. Folder batches additionally show the current item count.
20. Cancel selected or Cancel all stops unfinished transfers. Retry is available
    for failed or cancelled My files transfers in the current connection. Retry a
    File System batch from its upload or copy controls after checking completed
    items. Show in folder opens a completed download in Windows Explorer.
    Ctrl+F searches the current folder, Ctrl+L focuses its path, F5 refreshes,
    Ctrl+U uploads files, and Alt+Up goes to the parent folder.
21. Mark files or folders using the checkbox at the start of each row. Click the
    header checkbox or Select all to mark every visible item. Ctrl+A also marks
    visible items; Space toggles the focused item. Clear selection, Ctrl+Shift+A,
    or Escape in the file list clears the selection. In My files, the marked
    count above the list is also a Clear selection link. Ctrl/Shift row selection
    is available when no items are marked.
    Marks survive searching, sorting, filters and refreshing the same folder.
    Counts show items hidden by filters; actions still include those marked
    items. Changing folder or sidebar page clears that view's marks.
22. In File System, marked items can be copied, moved, downloaded, deleted or
    shared together. Rename requires one item. Multiple downloads use one local
    destination folder; each selected cloud folder becomes a ZIP. Existing local
    items are kept and new downloads receive names such as Report (2).pdf.
    Batch moves check destination names first and do not replace existing items.
    A cancelled or failed batch can leave earlier items completed. Refresh to
    check the current contents before trying again.
    Sharing several items first shows their existing links. Create missing links
    publishes read-only links for private items you can manage, while keeping
    existing link permissions. Review the listed targets before publishing.
23. File System action buttons use equal widths and switch between two and three
    rows as the window changes size. Gray and Dark use a static regular
    honeycomb grid with sparse cyan accents in the sidebar, heading and corner.
    The title pattern fades at its edges. The embedded File System summary uses
    a plain background so the heading motif does not restart below the title.
24. Statistics opens inside the main window. For File System, choose a folder
    (default /me), optionally include subfolders, then select Today, Last 7 days,
    Last 30 days or Custom range. From and To include both chosen local dates,
    up to 366 days. Select Refresh to apply dates or a new folder. Reading stats
    never downloads file contents or changes sharing permissions.
25. Downloaded only shows files with recorded downloads in the selected period.
    All results includes files with no downloads and items whose stats could not
    be read. Unavailable filters those errors. Search by name or path and sort by
    downloads, bandwidth, latest active interval or name. Summary cards show all
    successfully checked files in this scan, independently of the list filters.
26. Select a file to see its Downloads or Bandwidth chart, grouped daily or hourly.
    Hover over a bar for downloads, free transfer and premium transfer in that
    interval. Last active interval is a time bucket, not an individual download
    event. Counts and bandwidth follow PixelDrain's statistics definitions.
27. Progress shows the current file during a scan. Cancel preserves checked rows
    and identifies the totals as partial. Failed or inaccessible items are marked
    Unavailable, never reported as zero. Auto-refresh (60s) repeats a scan while
    Statistics is visible and no scan is already running; it is off by default.
    API requests use three workers. Rate-limit or authentication errors stop the
    scan. A large folder can take time; choose a smaller folder when needed.
28. My files in Statistics shows each file's lifetime totals and a lifetime bar.
    The API does not provide per-file date history for My files, so date controls
    are disabled for that source. These totals are not mixed with File System's
    date-filtered counts. No downloader names or emails are shown.
29. Export CSV saves the currently filtered and sorted rows with their period,
    download counts, bandwidth bytes, last active interval and availability.
    No API key is included. Right-click a File System statistics row to open its
    containing folder. Ctrl+F searches Statistics, F5 refreshes, Escape cancels a
    scan. Switching sidebar pages preserves this view; changing accounts clears
    it and cancels its pending requests.

Settings also lets you update your API key or disconnect your account.
Remember API key is enabled by default. After a successful connection, the key is
encrypted with Windows DPAPI for your Windows user account. Pixelroom tries to
connect automatically the next time you open it. No key is stored in the app folder.
Turn off Remember API key for a session-only connection. Turning it off or choosing
Disconnect removes the saved key. If you are offline, the key is kept so you can
select Refresh or reconnect from Settings later.
Collections and display names are saved on this computer.
File System access depends on your PixelDrain account and API key permissions.
PixelDrain currently requires a paid subscription for personal File System access:
https://docs.pixeldrain.com/filesystem/

Requires Windows and .NET Framework 4.8. The same EXE supports 32-bit and 64-bit Windows.
Keep Pixelroom.exe.config beside Pixelroom.exe.
