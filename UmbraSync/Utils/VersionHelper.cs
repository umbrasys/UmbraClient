using System.Net.Http.Headers;
using System.Reflection;

namespace UmbraSync.Utils;

public static class VersionHelper
{
    public const string UserAgentProduct = "MareSynchronos";

    public static string GetVersionString()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version == null) return "0.0.0";

        if (version.Build < 0)
            return $"{version.Major}.{version.Minor}";

        if (version.Revision < 0)
            return $"{version.Major}.{version.Minor}.{version.Build}";

        return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }

    public static ProductInfoHeaderValue GetUserAgentHeader()
    {
        return new ProductInfoHeaderValue(UserAgentProduct, GetVersionString());
    }
}
