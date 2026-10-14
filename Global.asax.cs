using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using PostProcessingServer.Services;

namespace PostProcessingServer
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            GlobalConfiguration.Configure(WebApiConfig.Register);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            // Start background job processor
            BackgroundJobWorker.Instance.Start();
            TileShareScheduleCleanupWorker.Start();
            TileShareActivityCleanupWorker.Start();
            System.Diagnostics.Trace.WriteLine("Shared background job worker started in Application_Start");
        }

        protected void Application_End()
        {
            // Stop background job processor gracefully
            BackgroundJobWorker.Instance.Stop(immediate: false);
            System.Diagnostics.Trace.WriteLine("Shared background job worker stopped in Application_End");
        }
    }
}
