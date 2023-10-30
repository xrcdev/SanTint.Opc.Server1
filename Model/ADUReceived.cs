using SQLite;

using System;

namespace SanTint.Opc.Server.Model
{
    /// <summary>
    /// 客户系统接收到的数据
    /// </summary>
    public class ADUReceived
    {
        #region 客户模型

        /// <summary>
        /// 工单号
        /// </summary>
        public string ProcessOrder { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public string DateTime { get; set; }

        /// <summary>
        /// 物料编码
        /// </summary>
        public string MaterialCode { get; set; }

        /// <summary>
        /// 实际用量
        /// </summary>
        public float ActualQuantity { get; set; }

        /// <summary>
        /// 单位
        /// </summary>
        /// <value>KG</value>
        public string QuantityUOM { get; set; }
        //public string LotText { get; set; }
        public string Lot { get; set; }
        public string PlantCode { get; set; }

        /// <summary>
        /// 设备标识
        /// </summary>
        public string DeviceIdentifier { get; set; }

        /// <summary>
        /// 容器
        /// </summary>
        public string VesselID { get; set; }

        public int ItemMaterial { get; set; }

        /// <summary>
        /// 批次号
        /// </summary>
        /// <value></value>
        public int BatchStepID { get; set; }

        public bool Watchdog { get; set; }

        #region 控制位
        public bool ReadyForNewDosing { get; set; }

        public bool DosingCompleted { get; set; }

        public bool RequestAccepted { get; set; }
        #endregion

        #endregion


        #region 三华补充字段
        [PrimaryKey, AutoIncrement]
        public int ID { get; set; }
        /// <summary>
        /// 数据表示 GUID
        /// </summary>
        [Unique]
        public string DataIdentifier { get; set; } = Guid.NewGuid().ToString().ToLowerInvariant();

        /// <summary>
        /// 计划用量
        /// </summary>
        public float Quantity { get; set; }

        /// <summary>
        /// 是否成功反馈给客户系统
        /// </summary>
        public bool IsComplete { get; set; }
        //public DateTime CompleteTime { get; set; }
        #endregion
    }

}
