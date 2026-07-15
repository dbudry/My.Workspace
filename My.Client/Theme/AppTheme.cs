using MudBlazor;

namespace My.Client.Theme;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

public static class AppTheme
{
    public const string ThemeModeStorageKey = "mw.theme.mode";

    public static MudTheme Build() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#2A5DB8",
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#2D7A3E",
            SecondaryContrastText = "#FFFFFF",
            Tertiary = "#B53A3A",
            TertiaryContrastText = "#FFFFFF",
            Info = "#2A5DB8",
            Success = "#2D7A3E",
            Warning = "#E08E2A",
            Error = "#B53A3A",

            Background = "#F5F7FA",
            Surface = "#FFFFFF",
            AppbarBackground = "#FFFFFF",
            AppbarText = "#1F2530",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#1F2530",
            DrawerIcon = "#5A6470",

            TextPrimary = "#1F2530",
            TextSecondary = "#5A6470",
            TextDisabled = "rgba(31,37,48,0.40)",
            ActionDefault = "#5A6470",

            LinesDefault = "#E2E6EC",
            LinesInputs = "#C8CFD8",
            TableLines = "#E2E6EC",
            TableStriped = "rgba(42,93,184,0.04)",
            TableHover = "rgba(42,93,184,0.08)",
            DividerLight = "#EDEFF3",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#5B8FE6",
            PrimaryContrastText = "#0E141C",
            Secondary = "#5FB374",
            SecondaryContrastText = "#0E141C",
            Tertiary = "#E07A7A",
            TertiaryContrastText = "#0E141C",
            Info = "#5B8FE6",
            Success = "#5FB374",
            Warning = "#F0B060",
            Error = "#E07A7A",

            Black = "#0E141C",
            Background = "#0E141C",
            BackgroundGray = "#161D27",
            Surface = "#161D27",
            AppbarBackground = "#161D27",
            AppbarText = "#E6E9EE",
            DrawerBackground = "#161D27",
            DrawerText = "#E6E9EE",
            DrawerIcon = "#A6AEBA",

            TextPrimary = "#E6E9EE",
            TextSecondary = "#A6AEBA",
            TextDisabled = "rgba(230,233,238,0.40)",
            ActionDefault = "#A6AEBA",

            LinesDefault = "#262E3B",
            LinesInputs = "#3A4250",
            TableLines = "#262E3B",
            TableStriped = "rgba(91,143,230,0.06)",
            TableHover = "rgba(91,143,230,0.12)",
            DividerLight = "#1F2733",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = new[] { "Roboto", "Helvetica Neue", "Helvetica", "Arial", "sans-serif" },
                FontSize = ".9rem",
                FontWeight = "400",
                LineHeight = "1.5",
                LetterSpacing = ".00938em",
            },
            H1 = new H1Typography { FontSize = "2.25rem", FontWeight = "600", LineHeight = "1.2" },
            H2 = new H2Typography { FontSize = "1.75rem", FontWeight = "600", LineHeight = "1.25" },
            H3 = new H3Typography { FontSize = "1.5rem",  FontWeight = "600", LineHeight = "1.3" },
            H4 = new H4Typography { FontSize = "1.25rem", FontWeight = "600", LineHeight = "1.35" },
            H5 = new H5Typography { FontSize = "1.1rem",  FontWeight = "600", LineHeight = "1.4" },
            H6 = new H6Typography { FontSize = "1rem",    FontWeight = "600", LineHeight = "1.4" },
            Button = new ButtonTypography { FontWeight = "600", TextTransform = "none" },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",
            DrawerWidthLeft = "260px",
        },
    };
}
