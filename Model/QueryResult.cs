using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SanTint.Opc.Server.Model
{
    public class QueryResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 错误编码 ,0或者空表示成功
        /// </summary>
        public string Code { get; set; }

        /// <summary>
        /// 消息提示
        /// </summary>
        public string Message { get; set; } = "";

        /// <summary>
        /// 返回数据,如列表数据
        /// </summary>
        public object Data { get; set; }

    }

}
