namespace AudioPriorityTray.Core;

/// <summary>Keywords used to detect headphone-like devices and auto-categorize them.</summary>
public static class HeadphoneDetection
{
    public static readonly IReadOnlyList<string> Keywords =
    [
        // Generic terms
        "headphone", "headset", "earphone", "earbud", "earbuds", "buds", "ear", "pods",

        // Apple
        "airpods", "earpods", "beats", "powerbeats", "beatsx", "beats fit", "beats solo", "beats studio",

        // Sony
        "wh-1000", "wf-1000", "linkbuds", "inzone",

        // Samsung
        "galaxy buds", "buds pro", "buds live", "buds fe",

        // Bose
        "quietcomfort", "qc ultra", "qc45", "qc35", "soundsport", "sport earbuds",

        // Sennheiser
        "momentum", "hd 4", "hd 5", "pxc",

        // Jabra
        "jabra", "elite", "evolve",

        // JBL
        "jbl tune", "jbl live", "jbl tour", "jbl reflect",

        // Other brands
        "anker", "soundcore", "skullcandy", "nothing ear", "oneplus buds", "pixel buds", "huawei freebuds",
        "oppo enco", "technics eah", "bowers", "b&w px", "denon perl", "focal bathys", "hifiman",
        "shure aonic", "audio-technica ath", "beyerdynamic", "marshall", "bang & olufsen", "b&o", "akg",
        "plantronics", "poly", "razer", "steelseries", "hyperx", "logitech g pro", "astro", "corsair",
        "1more", "tozo", "edifier", "fiio", "moondrop",
    ];

    public static bool IsHeadphone(string deviceName) =>
        Keywords.Any(keyword => deviceName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
}
