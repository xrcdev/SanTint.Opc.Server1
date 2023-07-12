using SQLite;

using System;

namespace SanTint.Opc.Server.Model
{
    /// <summary>
    /// 客户系统发送的数据
    /// </summary>
    public class ADUSent
    {

        #region 客户模型
        /// <summary>
        /// 工单号
        /// </summary> <summary>
        /// <value></value>
        public string ProcessOrder { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        /// <value></value>
        public DateTime DateTime { get; set; }

        /// <summary>
        /// 物料编码
        /// </summary>
        /// <value></value>
        public string MaterialCode { get; set; }

        /// <summary>
        /// 计划用量
        /// </summary>
        /// <value></value>
        public double Quantity { get; set; }

        /// <summary>
        /// 单位
        /// </summary> <summary>
        /// <value></value>
        public string QuantityUOM { get; set; }

        public int Lot { get; set; }

        public string PlantCode { get; set; }

        /// <summary>
        /// 设备标识
        /// </summary>
        /// <value></value>
        public string DeviceIdentifier { get; set; }

        /// <summary>
        /// 容器
        /// </summary>
        /// <value></value>
        public string VesselID { get; set; }

        public int ItemMaterial { get; set; }

        public int BatchStepID { get; set; }

        public bool Watchdog { get; set; }

        #region 控制位
        public bool NewDosingRequest { get; set; }
        public bool ConsumptionAccepted { get; set; }
        public bool ProcessOrderDataSent { get; set; }
        #endregion

        #endregion

        #region 三华补充字段
        [PrimaryKey, AutoIncrement]
        public int ID { get; set; }
        /// <summary>
        /// 数据表示 GUID
        /// </summary>
        /// <value> new GUID </value>
        [Unique]
        public string DataIdentifier { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 新增标识：三华系统是否已经完成/或者已经接收
        /// </summary>
        public bool IsSTComplete { get; set; }
        #endregion

    }

}
