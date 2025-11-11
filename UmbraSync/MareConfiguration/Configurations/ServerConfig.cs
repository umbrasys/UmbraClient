using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.WebAPI;

namespace MareSynchronos.MareConfiguration.Configurations;

[Serializable]
public class ServerConfig : IMareConfiguration
{
    public int CurrentServer { get; set; } = 0;

    public List<ServerStorage> ServerStorage { get; set; } = new()
    {
        { new ServerStorage() { ServerName = ApiController.UmbraServer, ServerUri = ApiController.UmbraServiceUri } },
    };

    public int Version { get; set; } = 1;
}