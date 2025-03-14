using AO;

public static class UIManager
{
    struct UIWindow
    {
        public Func<bool> DrawCall;
        public Vector2 Position;
        public bool Positional;
        public int Priority;
    }
    static List<UIWindow> WindowStack = new();
    public static void OpenUI(Func<bool> DrawCall, int priority = 0)
    {
        // Insert higher priority at the end, so they get drawn
        int index = WindowStack.FindIndex(window => window.Priority > priority);
        if (index == -1)
        {
            WindowStack.Add(new UIWindow { DrawCall = DrawCall, Positional = false, Priority = priority });
        }
        else
        {
            WindowStack.Insert(index, new UIWindow { DrawCall = DrawCall, Positional = false, Priority = priority });
        }
    }

    public static void OpenPositionalUI(Func<bool> OnDraw, Vector2 position)
    {
        WindowStack.Insert(0, new UIWindow { DrawCall = OnDraw, Positional = true, Position = position });
    }

    public static void CloseUI()
    {
        WindowStack.RemoveAt(WindowStack.Count - 1);
    }

    public static bool IsUIActive()
    {
        return WindowStack.Count > 0;
    }

    private static void DrawNext(Vector2 playerPos)
    {
        //Close current & draw next if possible
        CloseUI();
        DrawUI(playerPos);
    }

    public static void DrawUI(Vector2 playerPos)
    {
        if (WindowStack.Count == 0)
            return;
        var window = WindowStack[^1];
        if (window.Positional)
        {
            if (Vector2.Distance(window.Position, playerPos) > 3)
            {
                DrawNext(playerPos);
                return;
            }
        }
        if (!window.DrawCall())
        {
            DrawNext(playerPos);
        }
    }
}
