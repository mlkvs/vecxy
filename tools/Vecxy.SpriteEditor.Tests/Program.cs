using Vecxy.SpriteEditor;

TestTransparencyOrder();
TestMergeDistance();
TestGrid();
TestDocumentDragIsOneUndo();
Console.WriteLine("Sprite editor tests passed.");

static void TestTransparencyOrder()
{
    var pixels = new byte[12 * 8 * 4];
    Fill(pixels, 12, 1, 1, 2, 2);
    Fill(pixels, 12, 7, 1, 3, 2);
    Fill(pixels, 12, 2, 5, 2, 2);
    var slices = AutoSlicer.ByTransparency(pixels, 12, 8, new AutoSliceOptions(1, 1, 1, 0, 0));
    Equal(3, slices.Count, "island count");
    Equal((1, 1, 2, 2), Rect(slices[0]), "row-major first");
    Equal((7, 1, 3, 2), Rect(slices[1]), "row-major second");
    Equal((2, 5, 2, 2), Rect(slices[2]), "row-major third");
}

static void TestMergeDistance()
{
    var pixels = new byte[8 * 3 * 4];
    Fill(pixels, 8, 1, 1, 2, 1);
    Fill(pixels, 8, 4, 1, 2, 1);
    var slices = AutoSlicer.ByTransparency(pixels, 8, 3, new AutoSliceOptions(1, 1, 1, 0, 1));
    Equal(1, slices.Count, "nearby islands merge");
    Equal((1, 1, 5, 1), Rect(slices[0]), "merged bounds");
}

static void TestGrid()
{
    var slices = AutoSlicer.ByGrid(10, 7, new GridSliceOptions(3, 2, 1, 1, 1, 1));
    Equal(4, slices.Count, "grid count");
    Equal((1, 1, 3, 2), Rect(slices[0]), "grid first");
    Equal((5, 4, 3, 2), Rect(slices[3]), "grid last");
}

static void TestDocumentDragIsOneUndo()
{
    var atlas = new SpriteAtlas { Texture = "sheet.png" };
    atlas.Sprites["idle"] = new SpriteSlice { X = 1, Y = 2, Width = 3, Height = 4 };
    var document = new AtlasDocument();
    document.Open(atlas, "/tmp/sheet.png");
    document.BeginEdit();
    atlas.Sprites["idle"].X = 20;
    atlas.Sprites["idle"].X = 30;
    document.CommitEdit();
    Equal(true, document.CanUndo, "drag has undo");
    Equal(true, document.Undo(), "undo succeeds");
    Equal(1, document.Atlas.Sprites["idle"].X, "one undo restores drag start");
    Equal(false, document.CanUndo, "drag created one action");
}

static void Fill(byte[] pixels, int width, int x, int y, int w, int h)
{
    for (var py = y; py < y + h; py++)
    for (var px = x; px < x + w; px++) pixels[(py * width + px) * 4 + 3] = 255;
}

static (int, int, int, int) Rect(SpriteSlice slice) => (slice.X, slice.Y, slice.Width, slice.Height);

static void Equal<T>(T expected, T actual, string message) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
}
