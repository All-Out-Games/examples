using AO;

public static class PopupUI
{
    private static bool isShowing;
    private static float _percentageShowing;
    private static float percentageShowing;
    private static string currentTitle, currentMessage, currentAcceptText, currentRejectText;
    private static System.Action onAccept, onReject;



    private static UI.NineSlice defaultSlice = new UI.NineSlice()
    {
        slice = new Vector4(64, 64, 64, 64),
        sliceScale = 0.4f
    };

    public static void Show(string title, string message, Action onAcceptCallback = null, Action onRejectCallback = null, string acceptText = "Accept", string rejectText = "Reject")
    {
        if (!Network.IsClient) return;
        isShowing = true;
        currentTitle = title;
        currentMessage = message;
        onAccept = onAcceptCallback;
        onReject = onRejectCallback;
        currentAcceptText = acceptText;
        currentRejectText = rejectText;
        UIManager.OpenUI(DrawUI, 5);
    }


    public static bool DrawUI()
    {
        if (!Network.IsClient) return false;

        _percentageShowing += isShowing ? Time.DeltaTime * 2.0f : -Time.DeltaTime * 2.0f;
        if (_percentageShowing >= 1)
        {
            _percentageShowing = 1;
        }

        percentageShowing = Ease.InOutQuart(_percentageShowing);

        if (!isShowing && _percentageShowing <= 0)
        {
            _percentageShowing = 0;
            return false;
        }

        using var _col = UI.PUSH_COLOR_MULTIPLIER(new Vector4(1, 1, 1, percentageShowing));

        bool isMobile = Game.IsMobile;
        using var _ = UI.PUSH_SCALE_FACTOR(UI.ScreenScaleFactor * 1.5f, isMobile);

        var screenRect = UI.ScreenRect;
        //darken screen
        UI.Image(screenRect, UI.WhiteSprite, new Vector4(0, 0, 0, 0.75f));

        var popupRect = UI.ScreenRect.CenterRect().Grow(180, 280, 180, 280).Scale(percentageShowing);
        if (isMobile)
        {
            popupRect = popupRect.Offset(0, -40);
        }

        // Draw background panel
        UI.Image(popupRect, Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_8.png"), new Vector4(1, 1, 1, 0.95f), defaultSlice);

        // Draw buttons at the bottom first
        var buttonAreaRect = popupRect.CutBottom(40).Grow(0, -10, 0, -10).Offset(0, 10);
        var buttonRects = onReject != null
            ? UIUtils.VerticalSlice(buttonAreaRect, 2, 15)
            : new[] { buttonAreaRect.GrowUnscaled(0, -buttonAreaRect.Width * 0.25f, 0, -buttonAreaRect.Width * 0.25f) };


        var acceptSettings = new UI.ButtonSettings()
        {
            Sprite = Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_2.png"),
            Slice = defaultSlice,
            PressScaling = 0.95f
        };

        var rejectSettings = new UI.ButtonSettings()
        {
            Sprite = Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_5.png"),
            Slice = defaultSlice,
            PressScaling = 0.95f
        };

        var textSettings = UIUtils.CenteredText(true);

        if (UI.Button(buttonRects[0], currentAcceptText, acceptSettings, textSettings).Clicked)
        {
            isShowing = false;
            onAccept?.Invoke();
        }

        if (onReject != null && UI.Button(buttonRects[1], currentRejectText, rejectSettings, textSettings).Clicked)
        {
            isShowing = false;
            onReject?.Invoke();
        }

        var newSettings = UIUtils.CenteredText(true);
        newSettings.Size = 42 * percentageShowing; 
        newSettings.DoAutofit = false;

        // Draw title
        var titleRect = popupRect.CutTop(60).Inset(25);
        UI.Text(titleRect, currentTitle, newSettings);

        // Use remaining space for message
        var messageRect = popupRect.Inset(10);
        UI.Text(messageRect, currentMessage, new UI.TextSettings()
        {
            Size = 36 * percentageShowing,
            Color = Vector4.White,
            Font = UI.Fonts.BarlowBold,
            VerticalAlignment = UI.VerticalAlignment.Center,
            HorizontalAlignment = UI.HorizontalAlignment.Center,
            Outline = true,
            OutlineThickness = 2,
            WordWrap = true,
            DoAutofit = true,
            AutofitMinSize = 18 * percentageShowing,
            AutofitMaxSize = 42 * percentageShowing
        });
        return true;
    }
}