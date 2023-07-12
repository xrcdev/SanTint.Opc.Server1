using Newtonsoft.Json;

using SanTint.Common.Log;
using SanTint.Opc.Server.Model;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace SanTint.Opc.Server
{
    public class OrderController : ApiController
    {
        static DBHelper _dbHelper = new DBHelper();

        /// <summary>
        /// 查询待生产工单
        /// </summary>
        /// <param name="query">工单号或物料编码</param>
        /// <returns></returns>
        [HttpGet]
        public HttpResponseMessage GetProductionOrders(string query)
        {
            HttpResponseMessage httpReponseMessage = new HttpResponseMessage();
            try
            {
                QueryResult queryResult = new QueryResult();
                var result = _dbHelper.QueryADUSentByCode(query);
                queryResult.Data = result;
                queryResult.IsSuccess = true;
                var str = JsonConvert.SerializeObject(queryResult);
                httpReponseMessage.Content = new StringContent(str);
                httpReponseMessage.StatusCode = System.Net.HttpStatusCode.OK;
                return httpReponseMessage;
            }
            catch (Exception ex)
            {
                Logger.Write(ex.Message, category: Common.Utility.CategoryLog.Error);
                Logger.Write(ex, category: Common.Utility.CategoryLog.Error);

                QueryResult queryResult = new QueryResult();
                queryResult.Message = ex.Message;
                queryResult.Code = "500";
                var str = JsonConvert.SerializeObject(queryResult);

                httpReponseMessage.Content = new StringContent(str);
                httpReponseMessage.StatusCode = System.Net.HttpStatusCode.InternalServerError;
                return httpReponseMessage;
            }
        }

        /// <summary>
        /// 完成工单
        /// </summary>
        /// <param name="JsonStrData"></param>
        /// <returns></returns>
        [HttpPost]
        public HttpResponseMessage FinishOrder()
        {
            IEnumerable<MediaTypeFormatter> formatters = new MediaTypeFormatter[] { new JsonMediaTypeFormatter() };
            HttpResponseMessage httpReponseMessage = new HttpResponseMessage();
            QueryResult queryResult = new QueryResult();
            try
            {
                var order = this.Request.Content.ReadAsStringAsync().Result;
                Logger.Write("FinishOrder收到消息:" + order, category: Common.Utility.CategoryLog.Info);
                var received = this.Request.Content.ReadAsAsync<ADUReceived>(formatters).Result;

                //var received = JsonConvert.DeserializeObject<ADUReceived>(order);
                if (_dbHelper.IsDataIdentifierExistForADUReceived(received.DataIdentifier))
                {
                    queryResult = new QueryResult();
                    queryResult.IsSuccess = false;
                    queryResult.Code = "1001";
                    queryResult.Message = "";
                    var str1 = JsonConvert.SerializeObject(queryResult);
                    httpReponseMessage.Content = new StringContent(str1);
                    httpReponseMessage.StatusCode = System.Net.HttpStatusCode.OK;
                    return httpReponseMessage;
                }
                received.IsComplete = false;
                _dbHelper.AddADUReceived(received);

                //添加到队列,定时任务通知客户端
                QueueHelper.AddADUReceived(received);
                queryResult.IsSuccess = true;
                var str = JsonConvert.SerializeObject(queryResult);
                httpReponseMessage.Content = new StringContent(str);
                httpReponseMessage.StatusCode = System.Net.HttpStatusCode.OK;

                return httpReponseMessage;
            }
            catch (Exception ex)
            {
                Logger.Write(ex.Message, category: Common.Utility.CategoryLog.Error);
                Logger.Write(ex, category: Common.Utility.CategoryLog.Error);

                queryResult = new QueryResult();
                queryResult.Message = ex.Message;
                queryResult.Code = "500";
                var str = JsonConvert.SerializeObject(queryResult);

                httpReponseMessage.Content = new StringContent(str);
                httpReponseMessage.StatusCode = System.Net.HttpStatusCode.InternalServerError;
                return httpReponseMessage;
            }
        }

    }
}
