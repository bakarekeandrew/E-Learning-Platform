namespace E_Learning_Platform.Helpers
{
    public static class PdfConstants
    {
        public static class Colors
        {
            public const string Primary = "#007bff";
            public const string Secondary = "#6c757d";
            public const string Success = "#28a745";
            public const string Border = "#dee2e6";
            public const string Text = "#212529";
            public const string Gold = "#FFD700";
        }

        public static class PdfSizes
        {
            public const float LetterWidth = 8.5f * 72;  // 8.5 inches in points (Letter size)
            public const float LetterHeight = 11f * 72;  // 11 inches in points (Letter size)
            public const float DefaultMargin = 0.5f * 72; // 0.5 inch margin
        }
    }
} 