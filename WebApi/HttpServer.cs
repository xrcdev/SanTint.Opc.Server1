using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.SelfHost;

namespace SanTint.Opc.Server
{
    class HttpServer
    {
        static HttpSelfHostServer httpSelfHostServer;
        public HttpServer()
        {
            if (httpSelfHostServer == null)
            {
                //读取网站IP和端口号配置
                System.Configuration.Configuration config = System.Configuration.ConfigurationManager.OpenExeConfiguration(System.Configuration.ConfigurationUserLevel.None);
                var strIP = config.AppSettings.Settings["ApiIp"].Value;
                if (string.IsNullOrWhiteSpace(strIP)) strIP = "strIP";
                else strIP = strIP.Trim();
                strIP = strIP.TrimEnd('/');

                var strPort = config.AppSettings.Settings["ApiPort"].Value;
                if (string.IsNullOrWhiteSpace(strPort)) strPort = "8086";
                else strPort = strPort.Trim();

                var serverConfig = new HttpSelfHostConfiguration($"http://{strIP}:{strPort}");
                // 配置 http 服务的路由
                //var cors = new EnableCorsAttribute("*", "*", "*");//跨域允许设置
                //config.EnableCors(cors);
                serverConfig.Formatters.XmlFormatter.SupportedMediaTypes.Clear();

                //serverConfig.Formatters.Add(new PlainTextTypeFormatter());
                serverConfig.Routes.MapHttpRoute("DefaultApi", "Api/{controller}/{action}/{id}", defaults: new { id = RouteParameter.Optional });
                //serverConfig.MessageHandlers.Add(new CrosHandler());
                httpSelfHostServer = new HttpSelfHostServer(serverConfig);
            }
        }

        public void Start()
        {
            SanTint.Common.Log.Logger.Write("HttpServer即将启动");
            httpSelfHostServer.OpenAsync().Wait();
            SanTint.Common.Log.Logger.Write("HttpServer启动成功");
        }

        public void Stop()
        {
            SanTint.Common.Log.Logger.Write("HttpServer即将停止");
            httpSelfHostServer.CloseAsync().Wait();
            SanTint.Common.Log.Logger.Write("HttpServer停止成功");
        }

        public void Dispose()
        {
            httpSelfHostServer?.Dispose();
        }

    }
}
