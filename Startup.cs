using Microsoft.Owin;
using Owin;

//[assembly: OwinStartupAttribute(typeof(PostProcessingServer.Startup))]
namespace PostProcessingServer
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ConfigureAuth(app);
        }
    }
}
