
using LibUA;
using LibUA.Core;
using LibUA.Security.Cryptography.X509Certificates;
using LibUA.Security.Cryptography;

using Newtonsoft.Json.Linq;

using OPCUAServer;

using SanTint.Common.Log;
using SanTint.Opc.Server.Model;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.SelfHost;
using System.Xml.Linq;
using System.IO;
using RSACng = LibUA.Security.Cryptography.RSACng;
using Newtonsoft.Json;
using System.Configuration;

namespace SanTint.Opc.Server
{
    public class OpcServerApp
    {
        LibUA.Server.Master _server = default;
        public void Start()
        {
            SanTint.Common.Log.Logger.Write("OpcServer即将启动");

            OpcServer _app = new OpcServer();

            System.Configuration.Configuration config = System.Configuration.ConfigurationManager.OpenExeConfiguration(System.Configuration.ConfigurationUserLevel.None);
            var strIP = config.AppSettings.Settings["OpcIp"].Value;
            var strPort = config.AppSettings.Settings["OpcPort"].Value;
            if (string.IsNullOrWhiteSpace(strPort)) strPort = "8089";
            else strPort = strPort.Trim();
            _server = new LibUA.Server.Master(_app, Convert.ToInt32(strPort), 10, 30, 100, new OpcConsoleLogger());
            _server.Start();

            SanTint.Common.Log.Logger.Write("OpcServer启动成功");
        }


        public void Stop()
        {
            SanTint.Common.Log.Logger.Write("OpcServer即将关闭");
            _server.Stop();
            SanTint.Common.Log.Logger.Write("OpcServer停止成功");
        }
    }

    public class OpcServer : LibUA.Server.Application
    {
        static DBHelper _dbHelper = new DBHelper();

        NodeObject ItemsRoot = default;
        public ConcurrentDictionary<NodeId, Node> MyAddressSpaceTable;
        Dictionary<string, NodeVariable> AduSentDic = new Dictionary<string, NodeVariable>();
        Dictionary<string, NodeVariable> AduReceivedDic = new Dictionary<string, NodeVariable>();
        ApplicationDescription uaAppDesc;

        X509Certificate2 appCertificate = null;
        RSACryptoServiceProvider cryptPrivateKey = null;
        public override X509Certificate2 ApplicationCertificate
        {
            get { return appCertificate; }
        }

        public override RSACryptoServiceProvider ApplicationPrivateKey
        {
            get { return cryptPrivateKey; }
        }

        /// <summary>
        /// 初始化节点
        /// </summary>
        public OpcServer()
        {
            LoadCertificateAndPrivateKey();
            uaAppDesc = new ApplicationDescription(
              "urn:SanTintOpcServer",
              "http://SanTint.com/",
              new LocalizedText("en-US", "SanTint OPC UA Server"),
              ApplicationType.Server, null, null, null);

            var rootNodeId = new NodeId(2, "DataBlocksGlobal");
            ItemsRoot = new NodeObject(new NodeId(2, 0),
                    new QualifiedName("DataBlocksGlobal"),
                    new LocalizedText("DataBlocksGlobal"),
                    new LocalizedText("DataBlocksGlobal"), (uint)(0xFFFFFFFF), (uint)(0xFFFFFFFF), 0);

            AddressSpaceTable[new NodeId(UAConst.ObjectsFolder)].References
                .Add(new ReferenceNode(new NodeId(UAConst.Organizes), new NodeId(2, 0), false));
            ItemsRoot.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes),
                new NodeId(UAConst.ObjectsFolder), true));
            AddressSpaceTable.TryAdd(ItemsRoot.Id, ItemsRoot);

            AddAduSend();

            AddAduReceived();

            MyAddressSpaceTable = AddressSpaceTable;

            //timer
            //System.Configuration.Configuration config = System.Configuration.ConfigurationManager.OpenExeConfiguration(System.Configuration.ConfigurationUserLevel.None);
            //var strCheckNewMessageInterval = config.AppSettings.Settings["CheckNewMessageInterval"].Value;
            //if (string.IsNullOrWhiteSpace(strCheckNewMessageInterval)) strCheckNewMessageInterval = "1000";
            //else strCheckNewMessageInterval = strCheckNewMessageInterval.Trim();
            //_checkNewMessageInterval = Convert.ToInt32(strCheckNewMessageInterval);
            //_checkNewMessageTimer = new System.Threading.Timer((obj) => CheckNewMessage(), null, 1000, _checkNewMessageInterval);
            Task.Run(() => NotifyClientServerFinished());
            //_notifyClientServerFinishedTimer = new Timer((obj) => NotifyClientServerFinished(), null, _checkNewMessageInterval, _checkNewMessageInterval);
        }

        #region 私有方法

        private void NotifyClientServerFinished()
        {
            //_notifyClientServerFinishedTimer.Change(Timeout.Infinite, Timeout.Infinite);
            while (true)
            {
                Thread.Sleep(1000);

                try
                {
                    var aduS = new ADUSent();
                    var aduR = new ADUReceived();
                    var currentVal = AduReceivedDic[nameof(aduR.DosingCompleted)].Value;
                    var currentConsumptionAcceptedVal = AduSentDic[nameof(aduS.ConsumptionAccepted)].Value;
                    if (currentVal != null && Convert.ToBoolean(currentVal) &&
                        (currentConsumptionAcceptedVal != null && Convert.ToBoolean(currentConsumptionAcceptedVal) == false))
                    {
                        Logger.Write("ADUReceived正在反馈", category: Common.Utility.CategoryLog.Info);
                        continue;
                    }
                    else
                    {
                        var ADUReceiveds = _dbHelper.GetUncompleteADUReceived();

                        if (ADUReceiveds != null && ADUReceiveds.Any())
                        {
                            foreach (ADUReceived item in ADUReceiveds)
                            {
                                //设置节点值
                                AduReceivedDic[nameof(item.ProcessOrder)].Value = item.ProcessOrder;
                                AduReceivedDic[nameof(item.DateTime)].Value = item.DateTime;
                                AduReceivedDic[nameof(item.MaterialCode)].Value = item.MaterialCode;
                                AduReceivedDic[nameof(item.ActualQuantity)].Value = item.ActualQuantity;
                                AduReceivedDic[nameof(item.QuantityUOM)].Value = item.QuantityUOM;
                                AduReceivedDic[nameof(item.Lot)].Value = item.Lot;
                                AduReceivedDic[nameof(item.PlantCode)].Value = item.PlantCode;
                                AduReceivedDic[nameof(item.DeviceIdentifier)].Value = item.DeviceIdentifier;
                                AduReceivedDic[nameof(item.VesselID)].Value = item.VesselID;
                                AduReceivedDic[nameof(item.ItemMaterial)].Value = item.ItemMaterial;
                                AduReceivedDic[nameof(item.BatchStepID)].Value = item.BatchStepID;
                                AduReceivedDic[nameof(item.Watchdog)].Value = item.Watchdog;
                                //TODO:各项数据,应无须逐个通知客户端

                                //通知客户端,DosingCompleted=true
                                AduReceivedDic[nameof(item.DosingCompleted)].Value = true;
                                MonitorNotifyDataChange(AduReceivedDic[nameof(item.DosingCompleted)].Id, new DataValue(AduReceivedDic[nameof(item.DosingCompleted)].Value));

                                //等待客户端读取完成 ConsumptionAccepted=true
                                object consumptionAcceptedValue = false;
                                int maxTryTime = 5;
                                while (maxTryTime > 0)
                                {
                                    consumptionAcceptedValue = AduSentDic[nameof(aduS.ConsumptionAccepted)].Value;
                                    if (consumptionAcceptedValue == null || !Convert.ToBoolean(consumptionAcceptedValue))
                                    {
                                        consumptionAcceptedValue = AduSentDic[nameof(aduS.ConsumptionAccepted)].Value;
                                        maxTryTime -= 1;
                                        Thread.Sleep(500);
                                        continue;
                                    }
                                    Logger.Write("客户端已确认收到收到ADUReceived消息", category: Common.Utility.CategoryLog.Info);
                                    AduReceivedDic[nameof(item.DosingCompleted)].Value = false;
                                    MonitorNotifyDataChange(AduReceivedDic[nameof(item.DosingCompleted)].Id, new DataValue(AduReceivedDic[nameof(item.DosingCompleted)].Value));
                                    Logger.Write("重置,开始下一轮ADUReceived,成功通知客户端", category: Common.Utility.CategoryLog.Info);

                                    item.IsComplete = true;
                                    //item.CompleteTime = DateTime.Now;
                                    _dbHelper.UpdateADUReceived(item);
                                    break;
                                }
                                if (maxTryTime < 1)
                                {
                                    Logger.Write("等待一段时间后未收到客户端的ConsumptionAccepted=1的消息", category: Common.Utility.CategoryLog.Warn);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Write("检测ADUReceived是否有新数据时,出现异常:" + ex.Message, category: Common.Utility.CategoryLog.Error);
                    Logger.Write(ex, category: Common.Utility.CategoryLog.Error);
                }


            }

            //_notifyClientServerFinishedTimer.Change(1000, _checkNewMessageInterval);
        }

        private static bool CheckAduSendIsOk(ADUSent aduS)
        {
            //TODO:检测是否满足存库条件,根据实际情况完善
            if (string.IsNullOrEmpty(aduS.ProcessOrder))
                return false;
            if (string.IsNullOrEmpty(aduS.MaterialCode))
                return false;
            if (aduS.Quantity <= 0)
                return false;
            return true;
        }

        private void ConvertNodeToAduSend(ADUSent aduS)
        {
            try
            {
                var processOrder = AduSentDic[nameof(aduS.ProcessOrder)].Value;
                if (processOrder != null)
                {
                    aduS.ProcessOrder = processOrder.ToString();
                }
                var dateTime = AduSentDic[nameof(aduS.DateTime)].Value;
                if (dateTime != null)
                {
                    aduS.DateTime = Convert.ToDateTime(dateTime);
                }
                //add MaterialCode
                var materialCode = AduSentDic[nameof(aduS.MaterialCode)].Value;
                if (materialCode != null)
                {
                    aduS.MaterialCode = materialCode.ToString();
                }
                //Quantity
                var quantity = AduSentDic[nameof(aduS.Quantity)].Value;
                if (quantity != null)
                {
                    aduS.Quantity = Convert.ToDouble(quantity);
                }
                //QuantityUOM
                var quantityUOM = AduSentDic[nameof(aduS.QuantityUOM)].Value;
                if (quantityUOM != null)
                {
                    aduS.QuantityUOM = quantityUOM.ToString();
                }

                var lot = AduSentDic[nameof(aduS.Lot)].Value;
                if (lot != null)
                {
                    aduS.Lot = Convert.ToInt32(lot);
                }
                //add PlantCode
                var plantCode = AduSentDic[nameof(aduS.PlantCode)].Value;
                if (plantCode != null)
                {
                    aduS.PlantCode = plantCode.ToString();
                }

                var deviceIdentifier = AduSentDic[nameof(aduS.DeviceIdentifier)].Value;
                if (deviceIdentifier != null)
                {
                    aduS.DeviceIdentifier = deviceIdentifier.ToString();
                }
                // add VesselID
                var vesselID = AduSentDic[nameof(aduS.VesselID)].Value;
                if (vesselID != null)
                {
                    aduS.VesselID = vesselID.ToString();
                }
                //add ItemMaterial
                var itemMaterial = AduSentDic[nameof(aduS.ItemMaterial)].Value;
                if (itemMaterial != null)
                {
                    aduS.ItemMaterial = Convert.ToInt32(itemMaterial);
                }
                //add BatchStepID
                var batchStepID = AduSentDic[nameof(aduS.BatchStepID)].Value;
                if (batchStepID != null)
                {
                    aduS.BatchStepID = Convert.ToInt32(batchStepID);
                }

                var watchdog = AduSentDic[nameof(aduS.Watchdog)].Value;
                if (watchdog != null)
                {
                    aduS.Watchdog = Convert.ToBoolean(watchdog);
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine("将Node转换为ADUSent失败" + ex.Message);
#endif
                Logger.Write("将Node转换为ADUSent失败" + ex.Message, category: Common.Utility.CategoryLog.Error);
                Logger.Write(ex, category: Common.Utility.CategoryLog.Error);
            }

        }

        private void AddAduSend()
		{
			string prefix = "AduSend_";
			Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
			string aduSendPre = config.AppSettings.Settings["AduSendPre"].Value;
			bool flag = !string.IsNullOrWhiteSpace(aduSendPre);
			if (flag)
			{
				prefix = aduSendPre;
			}
			string aduNodeName = prefix.TrimEnd(new char[] { '.' }).TrimEnd(new char[] { '_' }).Replace("\"", "");
			NodeId aduSendNodeId = new NodeId(2, aduNodeName);
			NodeObject aduSend = new NodeObject(aduSendNodeId, new QualifiedName(aduNodeName), new LocalizedText(aduNodeName), new LocalizedText("AduSend Message Model"), uint.MaxValue, uint.MaxValue, 0);
			this.ItemsRoot.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), aduSend.Id, false));
			aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.ObjectsFolder), aduSend.Id, true));
			this.AddressSpaceTable.TryAdd(aduSend.Id, aduSend);
			ADUSent adu = new ADUSent();
			NodeVariable quantityNodel = new NodeVariable(new NodeId(2, prefix + "Quantity"), new QualifiedName("Quantity"), new LocalizedText("Quantity"), new LocalizedText("Quantity"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.Double), ValueRank.Scalar);
			aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), quantityNodel.Id, false));
			quantityNodel.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), quantityNodel.Id, true));
			this.AddressSpaceTable.TryAdd(quantityNodel.Id, quantityNodel);
			this.AduSentDic.Add("Quantity", quantityNodel);
			NodeVariable processOrderNode = new NodeVariable(new NodeId(2, prefix + "ProcessOrder".WrapQuote()), new QualifiedName("ProcessOrder"), new LocalizedText("ProcessOrder"), new LocalizedText("ProcessOrder"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.String), ValueRank.Scalar);
			aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), processOrderNode.Id, false));
			processOrderNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), processOrderNode.Id, true));
			this.AddressSpaceTable.TryAdd(processOrderNode.Id, processOrderNode);
			this.AduSentDic.Add("ProcessOrder", processOrderNode);
			NodeVariable dateTimeNode = new NodeVariable(new NodeId(2, prefix + "DateTime".WrapQuote()), new QualifiedName("DateTime"), new LocalizedText("DateTime"), new LocalizedText("DateTime"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 1000.0, false, new NodeId(UAConst.DateTime), ValueRank.Scalar);
			aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), dateTimeNode.Id, false));
			dateTimeNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), dateTimeNode.Id, true));
			this.AddressSpaceTable.TryAdd(dateTimeNode.Id, dateTimeNode);
			this.AduSentDic.Add("DateTime", dateTimeNode);
			NodeVariable MaterialCodeNode = new NodeVariable(new NodeId(2, prefix + "MaterialCode".WrapQuote()), new QualifiedName("MaterialCode"), new LocalizedText("MaterialCode"), new LocalizedText("MaterialCode"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.String), ValueRank.Scalar);
			aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), MaterialCodeNode.Id, false));
			MaterialCodeNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), MaterialCodeNode.Id, true));
			this.AddressSpaceTable.TryAdd(MaterialCodeNode.Id, MaterialCodeNode);
			this.AduSentDic.Add("MaterialCode", MaterialCodeNode);
			NodeVariable QuantityUOMNOde = new NodeVariable(new NodeId(2, prefix + "QuantityUOM".WrapQuote()), new QualifiedName("QuantityUOM"), new LocalizedText("QuantityUOM"), new LocalizedText("QuantityUOM"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.String), ValueRank.Scalar);
			aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), QuantityUOMNOde.Id, false));
			QuantityUOMNOde.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), QuantityUOMNOde.Id, true));
			this.AddressSpaceTable.TryAdd(QuantityUOMNOde.Id, QuantityUOMNOde);
			this.AduSentDic.Add("QuantityUOM", QuantityUOMNOde);
			NodeVariable lotNode = new NodeVariable(new NodeId(2, prefix + "Lot".WrapQuote()), new QualifiedName("Lot"), new LocalizedText("Lot"), new LocalizedText("Lot"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.Int32), ValueRank.Scalar);
			aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), lotNode.Id, false));
			lotNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), lotNode.Id, true));
			this.AddressSpaceTable.TryAdd(lotNode.Id, lotNode);
			this.AduSentDic.Add("Lot", lotNode);
			NodeVariable plantCodeNode = new NodeVariable(new NodeId(2, prefix + "PlantCode".WrapQuote()), new QualifiedName("PlantCode"), new LocalizedText("PlantCode"), new LocalizedText("PlantCode"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.String), ValueRank.Scalar);
			aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), plantCodeNode.Id, false));
			plantCodeNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), plantCodeNode.Id, true));
			this.AddressSpaceTable.TryAdd(plantCodeNode.Id, plantCodeNode);
			this.AduSentDic.Add("PlantCode", plantCodeNode);
			NodeVariable deviceIdentifierNode = new NodeVariable(new NodeId(2, prefix + "DeviceIdentifier".WrapQuote()), new QualifiedName("DeviceIdentifier"), new LocalizedText("DeviceIdentifier"), new LocalizedText("DeviceIdentifier"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.String), ValueRank.Scalar);
			aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), deviceIdentifierNode.Id, false));
			deviceIdentifierNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), deviceIdentifierNode.Id, true));
			this.AddressSpaceTable.TryAdd(deviceIdentifierNode.Id, deviceIdentifierNode);
			this.AduSentDic.Add("DeviceIdentifier", deviceIdentifierNode);
			NodeVariable vesselIDNode = new NodeVariable(new NodeId(2, prefix + "VesselID".WrapQuote()), new QualifiedName("VesselID"), new LocalizedText("VesselID"), new LocalizedText("VesselID"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.String), ValueRank.Scalar);
			aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), vesselIDNode.Id, false));
			vesselIDNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), vesselIDNode.Id, true));
			this.AddressSpaceTable.TryAdd(vesselIDNode.Id, vesselIDNode);
			this.AduSentDic.Add("VesselID", vesselIDNode);
			NodeVariable itemMaterialNode = new NodeVariable(new NodeId(2, prefix + "ItemMaterial".WrapQuote()), new QualifiedName("ItemMaterial"), new LocalizedText("ItemMaterial"), new LocalizedText("ItemMaterial"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.Int32), ValueRank.Scalar);
			aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), itemMaterialNode.Id, false));
			itemMaterialNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), itemMaterialNode.Id, true));
			this.AddressSpaceTable.TryAdd(itemMaterialNode.Id, itemMaterialNode);
			this.AduSentDic.Add("ItemMaterial", itemMaterialNode);
			NodeVariable batchStepIDNode = new NodeVariable(new NodeId(2, prefix + "BatchStepID".WrapQuote()), new QualifiedName("BatchStepID"), new LocalizedText("BatchStepID"), new LocalizedText("BatchStepID"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.Int32), ValueRank.Scalar);
			aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), batchStepIDNode.Id, false));
			batchStepIDNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), batchStepIDNode.Id, true));
			this.AddressSpaceTable.TryAdd(batchStepIDNode.Id, batchStepIDNode);
			this.AduSentDic.Add("BatchStepID", batchStepIDNode);
			NodeVariable newDosingRequestNode = new NodeVariable(new NodeId(2, prefix + "NewDosingRequest".WrapQuote()), new QualifiedName("NewDosingRequest"), new LocalizedText("NewDosingRequest"), new LocalizedText("NewDosingRequest"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.Boolean), ValueRank.Scalar);
			newDosingRequestNode.Value = false;
			aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), newDosingRequestNode.Id, false));
			newDosingRequestNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), newDosingRequestNode.Id, true));
			this.AddressSpaceTable.TryAdd(newDosingRequestNode.Id, newDosingRequestNode);
			this.AduSentDic.Add("NewDosingRequest", newDosingRequestNode);
			NodeVariable consumptionAcceptedNode = new NodeVariable(new NodeId(2, prefix + "ConsumptionAccepted".WrapQuote()), new QualifiedName("ConsumptionAccepted"), new LocalizedText("ConsumptionAccepted"), new LocalizedText("ConsumptionAccepted"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.Boolean), ValueRank.Scalar);
			consumptionAcceptedNode.Value = false;
			aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), consumptionAcceptedNode.Id, false));
			consumptionAcceptedNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), consumptionAcceptedNode.Id, true));
			this.AddressSpaceTable.TryAdd(consumptionAcceptedNode.Id, consumptionAcceptedNode);
			this.AduSentDic.Add("ConsumptionAccepted", consumptionAcceptedNode);
			NodeVariable processOrderDataSentNode = new NodeVariable(new NodeId(2, prefix + "ProcessOrderDataSent".WrapQuote()), new QualifiedName("ProcessOrderDataSent"), new LocalizedText("ProcessOrderDataSent"), new LocalizedText("ProcessOrderDataSent"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.Boolean), ValueRank.Scalar);
			processOrderDataSentNode.Value = false;
			aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), processOrderDataSentNode.Id, false));
			processOrderDataSentNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), processOrderDataSentNode.Id, true));
			this.AddressSpaceTable.TryAdd(processOrderDataSentNode.Id, processOrderDataSentNode);
			this.AduSentDic.Add("ProcessOrderDataSent", processOrderDataSentNode);
			NodeVariable watchdogNode = new NodeVariable(new NodeId(2, prefix + "Watchdog".WrapQuote()), new QualifiedName("Watchdog"), new LocalizedText("Watchdog"), new LocalizedText("Watchdog"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.Boolean), ValueRank.Scalar);
			aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), watchdogNode.Id, false));
			watchdogNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), watchdogNode.Id, true));
			this.AddressSpaceTable.TryAdd(watchdogNode.Id, watchdogNode);
			this.AduSentDic.Add("Watchdog", watchdogNode);
		}

         private void AddAduReceived()
		{
			string prefix = "AduReceived_";
			Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
			string preConfig = config.AppSettings.Settings["AduReceivedPre"].Value;
			bool flag = !string.IsNullOrWhiteSpace(preConfig);
			if (flag)
			{
				prefix = preConfig;
			}
			string aduNodeName = prefix.TrimEnd(new char[] { '.' }).TrimEnd(new char[] { '_' }).Replace("\"", "");
			NodeId ADUReceivedNodeId = new NodeId(2, aduNodeName);
			NodeObject ADUReceived = new NodeObject(ADUReceivedNodeId, new QualifiedName(aduNodeName), new LocalizedText(aduNodeName), new LocalizedText("ADUReceived Message Model"), 0U, 0U, 0);
			this.ItemsRoot.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), ADUReceived.Id, false));
			ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.ObjectsFolder), ADUReceived.Id, true));
			this.AddressSpaceTable.TryAdd(ADUReceived.Id, ADUReceived);
			ADUReceived adu = new ADUReceived();
			NodeVariable processOrderNode = new NodeVariable(new NodeId(2, prefix + "ProcessOrder".WrapQuote()), new QualifiedName("ProcessOrder"), new LocalizedText("ProcessOrder"), new LocalizedText("ProcessOrder"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.String), ValueRank.Scalar);
			ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), processOrderNode.Id, false));
			processOrderNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), processOrderNode.Id, true));
			this.AddressSpaceTable.TryAdd(processOrderNode.Id, processOrderNode);
			this.AduReceivedDic.Add("ProcessOrder", processOrderNode);
			NodeVariable dateTimeNode = new NodeVariable(new NodeId(2, prefix + "DateTime".WrapQuote()), new QualifiedName("DateTime"), new LocalizedText("DateTime"), new LocalizedText("DateTime"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.DateTime), ValueRank.Scalar);
			ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), dateTimeNode.Id, false));
			dateTimeNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), dateTimeNode.Id, true));
			this.AddressSpaceTable.TryAdd(dateTimeNode.Id, dateTimeNode);
			this.AduReceivedDic.Add("DateTime", dateTimeNode);
			NodeVariable materialCodeNode = new NodeVariable(new NodeId(2, prefix + "MaterialCode".WrapQuote()), new QualifiedName("MaterialCode"), new LocalizedText("MaterialCode"), new LocalizedText("MaterialCode"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.String), ValueRank.Scalar);
			ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), materialCodeNode.Id, false));
			materialCodeNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), materialCodeNode.Id, true));
			this.AddressSpaceTable.TryAdd(materialCodeNode.Id, materialCodeNode);
			this.AduReceivedDic.Add("MaterialCode", materialCodeNode);
			NodeVariable actualQuantityNode = new NodeVariable(new NodeId(2, prefix + "ActualQuantity".WrapQuote()), new QualifiedName("ActualQuantity"), new LocalizedText("ActualQuantity"), new LocalizedText("ActualQuantity"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.Double), ValueRank.Scalar);
			ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), actualQuantityNode.Id, false));
			actualQuantityNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), actualQuantityNode.Id, true));
			this.AddressSpaceTable.TryAdd(actualQuantityNode.Id, actualQuantityNode);
			this.AduReceivedDic.Add("ActualQuantity", actualQuantityNode);
			NodeVariable quantityUOMNode = new NodeVariable(new NodeId(2, prefix + "QuantityUOM".WrapQuote()), new QualifiedName("QuantityUOM"), new LocalizedText("QuantityUOM"), new LocalizedText("QuantityUOM"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.String), ValueRank.Scalar);
			ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), quantityUOMNode.Id, false));
			quantityUOMNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), quantityUOMNode.Id, true));
			this.AddressSpaceTable.TryAdd(quantityUOMNode.Id, quantityUOMNode);
			this.AduReceivedDic.Add("QuantityUOM", quantityUOMNode);
			NodeVariable lotNode = new NodeVariable(new NodeId(2, prefix + "Lot".WrapQuote()), new QualifiedName("Lot"), new LocalizedText("Lot"), new LocalizedText("Lot"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.Int32), ValueRank.Scalar);
			ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), lotNode.Id, false));
			lotNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), lotNode.Id, true));
			this.AddressSpaceTable.TryAdd(lotNode.Id, lotNode);
			this.AduReceivedDic.Add("Lot", lotNode);
			NodeVariable plantCodeNode = new NodeVariable(new NodeId(2, prefix + "PlantCode".WrapQuote()), new QualifiedName("PlantCode"), new LocalizedText("PlantCode"), new LocalizedText("PlantCode"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.String), ValueRank.Scalar);
			ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), plantCodeNode.Id, false));
			plantCodeNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), plantCodeNode.Id, true));
			this.AddressSpaceTable.TryAdd(plantCodeNode.Id, plantCodeNode);
			this.AduReceivedDic.Add("PlantCode", plantCodeNode);
			NodeVariable deviceIdentifierNode = new NodeVariable(new NodeId(2, prefix + "DeviceIdentifier".WrapQuote()), new QualifiedName("DeviceIdentifier"), new LocalizedText("DeviceIdentifier"), new LocalizedText("DeviceIdentifier"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.String), ValueRank.Scalar);
			ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), deviceIdentifierNode.Id, false));
			deviceIdentifierNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), deviceIdentifierNode.Id, true));
			this.AddressSpaceTable.TryAdd(deviceIdentifierNode.Id, deviceIdentifierNode);
			this.AduReceivedDic.Add("DeviceIdentifier", deviceIdentifierNode);
			NodeVariable vesselIDNode = new NodeVariable(new NodeId(2, prefix + "VesselID".WrapQuote()), new QualifiedName("VesselID"), new LocalizedText("VesselID"), new LocalizedText("VesselID"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.String), ValueRank.Scalar);
			ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), vesselIDNode.Id, false));
			vesselIDNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), vesselIDNode.Id, true));
			this.AddressSpaceTable.TryAdd(vesselIDNode.Id, vesselIDNode);
			this.AduReceivedDic.Add("VesselID", vesselIDNode);
			NodeVariable itemMaterialNode = new NodeVariable(new NodeId(2, prefix + "ItemMaterial".WrapQuote()), new QualifiedName("ItemMaterial"), new LocalizedText("ItemMaterial"), new LocalizedText("ItemMaterial"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.Int32), ValueRank.Scalar);
			ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), itemMaterialNode.Id, false));
			itemMaterialNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), itemMaterialNode.Id, true));
			this.AddressSpaceTable.TryAdd(itemMaterialNode.Id, itemMaterialNode);
			this.AduReceivedDic.Add("ItemMaterial", itemMaterialNode);
			NodeVariable batchStepIDNode = new NodeVariable(new NodeId(2, prefix + "BatchStepID".WrapQuote()), new QualifiedName("BatchStepID"), new LocalizedText("BatchStepID"), new LocalizedText("BatchStepID"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.Int32), ValueRank.Scalar);
			ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), batchStepIDNode.Id, false));
			batchStepIDNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), batchStepIDNode.Id, true));
			this.AddressSpaceTable.TryAdd(batchStepIDNode.Id, batchStepIDNode);
			this.AduReceivedDic.Add("BatchStepID", batchStepIDNode);
			NodeVariable readyForNewDosingNode = new NodeVariable(new NodeId(2, prefix + "ReadyForNewDosing".WrapQuote()), new QualifiedName("ReadyForNewDosing"), new LocalizedText("ReadyForNewDosing"), new LocalizedText("ReadyForNewDosing"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.Boolean), ValueRank.Scalar);
			readyForNewDosingNode.Value = false;
			ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), readyForNewDosingNode.Id, false));
			readyForNewDosingNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), readyForNewDosingNode.Id, true));
			this.AddressSpaceTable.TryAdd(readyForNewDosingNode.Id, readyForNewDosingNode);
			this.AduReceivedDic.Add("ReadyForNewDosing", readyForNewDosingNode);
			NodeVariable dosingCompletedNode = new NodeVariable(new NodeId(2, prefix + "DosingCompleted".WrapQuote()), new QualifiedName("DosingCompleted"), new LocalizedText("DosingCompleted"), new LocalizedText("DosingCompleted"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.Boolean), ValueRank.Scalar);
			dosingCompletedNode.Value = false;
			ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), dosingCompletedNode.Id, false));
			dosingCompletedNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), dosingCompletedNode.Id, true));
			this.AddressSpaceTable.TryAdd(dosingCompletedNode.Id, dosingCompletedNode);
			this.AduReceivedDic.Add("DosingCompleted", dosingCompletedNode);
			NodeVariable requestAcceptedNode = new NodeVariable(new NodeId(2, prefix + "RequestAccepted".WrapQuote()), new QualifiedName("RequestAccepted"), new LocalizedText("RequestAccepted"), new LocalizedText("RequestAccepted"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.Boolean), ValueRank.Scalar);
			requestAcceptedNode.Value = true;
			ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), requestAcceptedNode.Id, false));
			requestAcceptedNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), requestAcceptedNode.Id, true));
			this.AddressSpaceTable.TryAdd(requestAcceptedNode.Id, requestAcceptedNode);
			this.AduReceivedDic.Add("RequestAccepted", requestAcceptedNode);
			NodeVariable watchdogNode = new NodeVariable(new NodeId(2, prefix + "Watchdog".WrapQuote()), new QualifiedName("Watchdog"), new LocalizedText("Watchdog"), new LocalizedText("Watchdog"), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(UAConst.Boolean), ValueRank.Scalar);
			ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), watchdogNode.Id, false));
			watchdogNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), watchdogNode.Id, true));
			this.AddressSpaceTable.TryAdd(watchdogNode.Id, watchdogNode);
			this.AduReceivedDic.Add("Watchdog", watchdogNode);
		}
        private void LoadCertificateAndPrivateKey()
        {
            try
            {
                // Try to load existing (public key) and associated private key
                appCertificate = new X509Certificate2("ServerCert.der");
                cryptPrivateKey = new RSACryptoServiceProvider();

                var rsaPrivParams = UASecurity.ImportRSAPrivateKey(File.ReadAllText("ServerKey.pem"));
                cryptPrivateKey.ImportParameters(rsaPrivParams);
            }
            catch
            {
                // Make a new certificate (public key) and associated private key
                var dn = new X500DistinguishedName("CN=Client certificate;OU=Demo organization", X500DistinguishedNameFlags.UseSemicolons);

                var keyCreationParameters = new CngKeyCreationParameters()
                {
                    KeyUsage = CngKeyUsages.AllUsages,
                    KeyCreationOptions = CngKeyCreationOptions.OverwriteExistingKey,
                    ExportPolicy = CngExportPolicies.AllowPlaintextExport
                };

                keyCreationParameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(1024), CngPropertyOptions.None));
                var cngKey = CngKey.Create(CngAlgorithm2.Rsa, "KeyName", keyCreationParameters);

                var certParams = new X509CertificateCreationParameters(dn)
                {
                    StartTime = DateTime.Now,
                    EndTime = DateTime.Now.AddYears(10),
                    SignatureAlgorithm = X509CertificateSignatureAlgorithm.RsaSha1,
                    TakeOwnershipOfKey = true
                };

                appCertificate = cngKey.CreateSelfSignedCertificate(certParams);

                var certPrivateCNG = new RSACng(appCertificate.GetCngPrivateKey());
                var certPrivateParams = certPrivateCNG.ExportParameters(true);

                File.WriteAllText("ServerCert.der", UASecurity.ExportPEM(appCertificate));
                File.WriteAllText("ServerKey.pem", UASecurity.ExportRSAPrivateKey(certPrivateParams));

                cryptPrivateKey = new RSACryptoServiceProvider();
                cryptPrivateKey.ImportParameters(certPrivateParams);
            }
        }
        #endregion


        #region Overrides of UAServer

        /// <summary>
        /// 浏览某个节点
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        protected override DataValue HandleReadRequestInternal(NodeId id)
        {
            Node node = null;
            bool flag = id.NamespaceIndex == 2 && this.AddressSpaceTable.TryGetValue(id, out node);
            if (flag)
            {
                NodeVariable nodeVariable = node as NodeVariable;
                bool flag2 = nodeVariable != null;
                if (flag2)
                {
                    switch (nodeVariable.DataType.NumericIdentifier)
                    {
                        case 1:
                            {
                                bool flag3 = nodeVariable.Value == null;
                                if (flag3)
                                {
                                    nodeVariable.Value = new DataValue(false, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 2:
                            {
                                bool flag4 = nodeVariable.Value == null;
                                if (flag4)
                                {
                                    nodeVariable.Value = new DataValue(0, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 3:
                            {
                                bool flag5 = nodeVariable.Value == null;
                                if (flag5)
                                {
                                    nodeVariable.Value = new DataValue(0, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 4:
                            {
                                bool flag6 = nodeVariable.Value == null;
                                if (flag6)
                                {
                                    nodeVariable.Value = new DataValue(0, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 5:
                            {
                                bool flag7 = nodeVariable.Value == null;
                                if (flag7)
                                {
                                    nodeVariable.Value = new DataValue(0, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 6:
                            {
                                bool flag8 = nodeVariable.Value == null;
                                if (flag8)
                                {
                                    nodeVariable.Value = new DataValue(0, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 7:
                            {
                                bool flag9 = nodeVariable.Value == null;
                                if (flag9)
                                {
                                    nodeVariable.Value = new DataValue(0, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 8:
                            {
                                bool flag10 = nodeVariable.Value == null;
                                if (flag10)
                                {
                                    nodeVariable.Value = new DataValue(0L, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 9:
                            {
                                bool flag11 = nodeVariable.Value == null;
                                if (flag11)
                                {
                                    nodeVariable.Value = new DataValue(0L, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 10:
                            {
                                bool flag12 = nodeVariable.Value == null;
                                if (flag12)
                                {
                                    nodeVariable.Value = new DataValue(0f, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 11:
                            {
                                bool flag13 = nodeVariable.Value == null;
                                if (flag13)
                                {
                                    nodeVariable.Value = new DataValue(0.0, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 12:
                            {
                                bool flag14 = nodeVariable.Value == null;
                                if (flag14)
                                {
                                    nodeVariable.Value = new DataValue(string.Empty, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 13:
                            {
                                bool flag15 = nodeVariable.Value == null;
                                if (flag15)
                                {
                                    nodeVariable.Value = new DataValue(new DateTime(1900, 1, 1), new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 14:
                            {
                                bool flag16 = nodeVariable.Value == null;
                                if (flag16)
                                {
                                    nodeVariable.Value = new DataValue(Guid.NewGuid(), new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 15:
                            {
                                bool flag17 = nodeVariable.Value == null;
                                if (flag17)
                                {
                                    nodeVariable.Value = new DataValue(new byte[0], new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 16:
                            {
                                bool flag18 = nodeVariable.Value == null;
                                if (flag18)
                                {
                                    nodeVariable.Value = new DataValue(new XElement("element"), new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 19:
                            {
                                bool flag19 = nodeVariable.Value == null;
                                if (flag19)
                                {
                                    nodeVariable.Value = new DataValue(StatusCode.Good, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 20:
                            {
                                bool flag20 = nodeVariable.Value == null;
                                if (flag20)
                                {
                                    nodeVariable.Value = new DataValue(default(QualifiedName), new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 21:
                            {
                                bool flag21 = nodeVariable.Value == null;
                                if (flag21)
                                {
                                    nodeVariable.Value = new DataValue(new LocalizedText(""), new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                        case 22:
                            {
                                bool flag22 = nodeVariable.Value == null;
                                if (flag22)
                                {
                                    nodeVariable.Value = new DataValue(new ExtensionObject(), new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                                }
                                return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                            }
                    }
                }
            }
            return base.HandleReadRequestInternal(id);
        }

        /// <summary>
        /// 写节点
        /// </summary>
        /// <param name="session"></param>
        /// <param name="writeValues"></param>
        /// <returns></returns>
        public override uint[] HandleWriteRequest(object session, WriteValue[] writeValues)
        {
            if (writeValues.Length > 0)
            {
                try
                {
                    foreach (var item in writeValues)
                    {
                        var nodeVariable = AddressSpaceTable[item.NodeId] as NodeVariable;
                        nodeVariable.Value = item.Value.Value;
                        var aduR = new ADUReceived();

                        if (item.NodeId.StringIdentifier.Contains("NewDosingRequest"))
                        {
                            if (Convert.ToBoolean(nodeVariable.Value))
                            {
                                AduReceivedDic[nameof(aduR.RequestAccepted)].Value = false;
                                AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Value = true;
                                MonitorNotifyDataChange(AduReceivedDic[nameof(aduR.RequestAccepted)].Id, new DataValue(AduReceivedDic[nameof(aduR.RequestAccepted)].Value));
                                MonitorNotifyDataChange(AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Id, new DataValue(AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Value));
                                Logger.Write("服务端准备接收,并成功通知客户端", category: Common.Utility.CategoryLog.Info);
                            }
                            //AddressSpaceTable[node]
                        }
                        else if (item.NodeId.StringIdentifier.Contains("ProcessOrderDataSent"))
                        {
                            if (Convert.ToBoolean(nodeVariable.Value))
                            {
                                //读取AduSend各节点数据,保存到数据库
                                var aduS = new ADUSent();
                                ConvertNodeToAduSend(aduS);

                                if (CheckAduSendIsOk(aduS))
                                {
                                    Logger.Write("ADUSent新增数据添加到数据库:" + JsonConvert.SerializeObject(aduS), category: Common.Utility.CategoryLog.Info);
                                    _dbHelper.AddADUSent(aduS);

                                    //接收完成,通知客户端
                                    AduReceivedDic[nameof(aduR.RequestAccepted)].Value = true;
                                    //AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Value = false;
                                    MonitorNotifyDataChange(AduReceivedDic[nameof(aduR.RequestAccepted)].Id, new DataValue(AduReceivedDic[nameof(aduR.RequestAccepted)].Value));
                                    //MonitorNotifyDataChange(AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Id, new DataValue(AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Value));
                                    Logger.Write("ADUSent新增数据添加数据库,并成功通知客户端", category: Common.Utility.CategoryLog.Info);
                                }
                                else
                                {
                                    //TODO:是否有这种情况?发送数据不合规范
                                    //接收完成,通知客户端
                                    //AduReceivedDic[nameof(aduR.RequestAccepted)].Value = true;
                                    //AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Value = false;
                                    //MonitorNotifyDataChange(AduReceivedDic[nameof(aduR.RequestAccepted)].Id, new DataValue(AduReceivedDic[nameof(aduR.RequestAccepted)].Value));
                                    //MonitorNotifyDataChange(AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Id, new DataValue(AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Value));
                                    Logger.Write("ADUSent新增数据不合规范:" + JsonConvert.SerializeObject(aduS), category: Common.Utility.CategoryLog.Error);
                                    //Logger.Write("ADUSent新增数据未添加到数据库,成功通知客户端", category: Common.Utility.CategoryLog.Info);
                                }
                            }
                        }

                        //else if (item.NodeId.StringIdentifier.Contains("ConsumptionAccepted"))
                        //{
                        //    if (Convert.ToBoolean(nodeVariable.Value))
                        //    {
                        //        Logger.Write("客户端已确认收到收到ADUReceived消息", category: Common.Utility.CategoryLog.Info);

                        //        AduReceivedDic[nameof(aduR.DosingCompleted)].Value = false;
                        //        MonitorNotifyDataChange(AduReceivedDic[nameof(aduR.DosingCompleted)].Id, new DataValue(AduReceivedDic[nameof(aduR.DosingCompleted)].Value));
                        //        Logger.Write("重置,开始下一轮ADUReceived,成功通知客户端", category: Common.Utility.CategoryLog.Info);
                        //    }
                        //}

                    }
                    var respStatus = new UInt32[writeValues.Length];
                    for (int i = 0; i < writeValues.Length; i++)
                    {
                        respStatus[i] = (UInt32)StatusCode.Good;
                    }
                    return respStatus;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            return base.HandleWriteRequest(session, writeValues);
        }

        public override object SessionCreate(SessionCreationInfo sessionInfo)
        {
            // Optionally create and return a session object with sessionInfo if you want to track that same object
            // when the client validates its session (anonymous, username + password or certificate).

            return null;
        }


        public override bool SessionValidateClientApplication(object session,
            ApplicationDescription clientApplicationDescription, byte[] clientCertificate, string sessionName)
        {
            // Update your session object with the client's UA application description
            // Return true to allow the client, false to reject

            return true;
        }

        public override void SessionRelease(object session)
        {
        }

        public override bool SessionValidateClientUser(object session, object userIdentityToken)
        {
            if (userIdentityToken is UserIdentityAnonymousToken)
            {
                return true;
            }
            else if (userIdentityToken is UserIdentityUsernameToken)
            {
                var username = (userIdentityToken as UserIdentityUsernameToken).Username;
                var password =
                    (new UTF8Encoding()).GetString((userIdentityToken as UserIdentityUsernameToken).PasswordHash);

                return true;
            }

            throw new Exception("Unhandled user identity token type");
        }

        private ApplicationDescription CreateApplicationDescriptionFromEndpointHint(string endpointUrlHint)
        {
            string[] discoveryUrls = uaAppDesc.DiscoveryUrls;
            if (discoveryUrls == null && !string.IsNullOrEmpty(endpointUrlHint))
            {
                discoveryUrls = new string[] { endpointUrlHint };
            }

            return new ApplicationDescription(uaAppDesc.ApplicationUri, uaAppDesc.ProductUri, uaAppDesc.ApplicationName,
                uaAppDesc.Type, uaAppDesc.GatewayServerUri, uaAppDesc.DiscoveryProfileUri, discoveryUrls);
        }

        public override IList<EndpointDescription> GetEndpointDescriptions(string endpointUrlHint)
        {
            var certStr = ApplicationCertificate.Export(X509ContentType.Cert);
            ApplicationDescription localAppDesc = CreateApplicationDescriptionFromEndpointHint(endpointUrlHint);

            var epNoSecurity = new EndpointDescription(
                endpointUrlHint, localAppDesc, certStr,
                MessageSecurityMode.None, Types.SLSecurityPolicyUris[(int)SecurityPolicy.None],
                new UserTokenPolicy[]
                {
                        new UserTokenPolicy("0", UserTokenType.Anonymous, null, null, Types.SLSecurityPolicyUris[(int)SecurityPolicy.None]),
                        new UserTokenPolicy("1", UserTokenType.UserName, null, null, Types.SLSecurityPolicyUris[(int)SecurityPolicy.Basic256Sha256]),
                }, Types.TransportProfileBinary, 0);

            var epSignBasic128Rsa15 = new EndpointDescription(
                endpointUrlHint, localAppDesc, certStr,
                MessageSecurityMode.Sign, Types.SLSecurityPolicyUris[(int)SecurityPolicy.Basic128Rsa15],
                new UserTokenPolicy[]
                {
                        new UserTokenPolicy("0", UserTokenType.Anonymous, null, null, Types.SLSecurityPolicyUris[(int)SecurityPolicy.None]),
                        new UserTokenPolicy("1", UserTokenType.UserName, null, null, Types.SLSecurityPolicyUris[(int)SecurityPolicy.Basic256Sha256]),
                }, Types.TransportProfileBinary, 0);

            var epSignBasic256 = new EndpointDescription(
                endpointUrlHint, localAppDesc, certStr,
                MessageSecurityMode.Sign, Types.SLSecurityPolicyUris[(int)SecurityPolicy.Basic256],
                new UserTokenPolicy[]
                {
                        new UserTokenPolicy("0", UserTokenType.Anonymous, null, null, Types.SLSecurityPolicyUris[(int)SecurityPolicy.None]),
                        new UserTokenPolicy("1", UserTokenType.UserName, null, null, Types.SLSecurityPolicyUris[(int)SecurityPolicy.Basic256Sha256]),
                }, Types.TransportProfileBinary, 0);

            var epSignBasic256Sha256 = new EndpointDescription(
                endpointUrlHint, localAppDesc, certStr,
                MessageSecurityMode.Sign, Types.SLSecurityPolicyUris[(int)SecurityPolicy.Basic256Sha256],
                new UserTokenPolicy[]
                {
                        new UserTokenPolicy("0", UserTokenType.Anonymous, null, null, Types.SLSecurityPolicyUris[(int)SecurityPolicy.None]),
                        new UserTokenPolicy("1", UserTokenType.UserName, null, null, Types.SLSecurityPolicyUris[(int)SecurityPolicy.Basic256Sha256]),
                }, Types.TransportProfileBinary, 0);

            var epSignEncryptBasic128Rsa15 = new EndpointDescription(
                endpointUrlHint, localAppDesc, certStr,
                MessageSecurityMode.SignAndEncrypt, Types.SLSecurityPolicyUris[(int)SecurityPolicy.Basic128Rsa15],
                new UserTokenPolicy[]
                {
                        new UserTokenPolicy("0", UserTokenType.Anonymous, null, null, Types.SLSecurityPolicyUris[(int)SecurityPolicy.None]),
                        new UserTokenPolicy("1", UserTokenType.UserName, null, null, Types.SLSecurityPolicyUris[(int)SecurityPolicy.Basic256Sha256]),
                }, Types.TransportProfileBinary, 0);

            var epSignEncryptBasic256 = new EndpointDescription(
                endpointUrlHint, localAppDesc, certStr,
                MessageSecurityMode.SignAndEncrypt, Types.SLSecurityPolicyUris[(int)SecurityPolicy.Basic256],
                new UserTokenPolicy[]
                {
                        new UserTokenPolicy("0", UserTokenType.Anonymous, null, null, Types.SLSecurityPolicyUris[(int)SecurityPolicy.None]),
                        new UserTokenPolicy("1", UserTokenType.UserName, null, null, Types.SLSecurityPolicyUris[(int)SecurityPolicy.Basic256Sha256]),
                }, Types.TransportProfileBinary, 0);

            var epSignEncryptBasic256Sha256 = new EndpointDescription(
                endpointUrlHint, localAppDesc, certStr,
                MessageSecurityMode.SignAndEncrypt, Types.SLSecurityPolicyUris[(int)SecurityPolicy.Basic256Sha256],
                new UserTokenPolicy[]
                {
                        new UserTokenPolicy("0", UserTokenType.Anonymous, null, null, Types.SLSecurityPolicyUris[(int)SecurityPolicy.None]),
                        new UserTokenPolicy("1", UserTokenType.UserName, null, null, Types.SLSecurityPolicyUris[(int)SecurityPolicy.Basic256Sha256]),
                }, Types.TransportProfileBinary, 0);

            return new EndpointDescription[]
            {
                    epNoSecurity,
                    epSignBasic256Sha256, epSignEncryptBasic256Sha256,
                    epSignBasic128Rsa15, epSignEncryptBasic128Rsa15,
                    epSignBasic256, epSignEncryptBasic256
            };
        }

        public override ApplicationDescription GetApplicationDescription(string endpointUrlHint)
        {
            return CreateApplicationDescriptionFromEndpointHint(endpointUrlHint);
        }

        #endregion

    }

    class OpcConsoleLogger : ILogger
    {
        public bool HasLevel(LogLevel Level)
        {
            return true;
        }

        public void LevelSet(LogLevel Mask)
        {
        }

        public void Log(LogLevel Level, string Str)
        {
#if DEBUG
            Console.WriteLine("OpcServer:[{0}] {1}", Level.ToString(), Str);
#endif
            Logger.Write(string.Format("OpcServer:[{0}] {1}", Level.ToString(), Str), Common.Utility.CategoryLog.Info);
        }
    }
}
