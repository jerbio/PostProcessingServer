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
            AnalysisJobProcessor.Instance.Start();
            System.Diagnostics.Trace.WriteLine("Analysis Job Processor started in Application_Start");
        }

        protected void Application_End()
        {
            // Stop background job processor gracefully
            AnalysisJobProcessor.Instance.Stop(immediate: false);
            System.Diagnostics.Trace.WriteLine("Analysis Job Processor stopped in Application_End");
        }
    }
}
