
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
        //System.Threading.Timer _checkNewMessageTimer = default;
        System.Threading.Timer _notifyClientServerFinishedTimer = default;
        Dictionary<string, NodeVariable> AduSentDic = new Dictionary<string, NodeVariable>();
        Dictionary<string, NodeVariable> AduReceivedDic = new Dictionary<string, NodeVariable>();
        int _checkNewMessageInterval = 1000;
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

            _notifyClientServerFinishedTimer = new Timer((obj) => NotifyClientServerFinished(), null, 1000, _checkNewMessageInterval);
        }


        //private void CheckNewMessage()
        //{
        //    _checkNewMessageTimer.Change(Timeout.Infinite, Timeout.Infinite);
        //    try
        //    {
        //        var aduS = new ADUSent();
        //        var aduR = new ADUReceived();
        //        var value = AduSentDic[nameof(aduS.NewDosingRequest)].Value;
        //        if (value != null && Convert.ToBoolean(value))
        //        {
        //            AduReceivedDic[nameof(aduR.RequestAccepted)].Value = false;
        //            AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Value = true;
        //            MonitorNotifyDataChange(AduReceivedDic[nameof(aduR.RequestAccepted)].Id, new DataValue(AduReceivedDic[nameof(aduR.RequestAccepted)].Value));
        //            MonitorNotifyDataChange(AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Id, new DataValue(AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Value));
        //            Logger.Write("服务端准备接收,并成功通知客户端", category: Common.Utility.CategoryLog.Info);

        //            //等待客户端发送完成 ProcessOrderDataSent=true
        //            while (true)
        //            {
        //                var value1 = AduSentDic[nameof(aduS.ProcessOrderDataSent)].Value;
        //                if (value1 != null && Convert.ToBoolean(value1))
        //                {
        //                    //读取AduSend各节点数据,保存到数据库
        //                    ConvertNodeToAduSend(aduS);

        //                    if (CheckAduSendIsOk(aduS))
        //                    {
        //                        Logger.Write("ADUSent新增到数据库", category: Common.Utility.CategoryLog.Info);
        //                        _dbHelper.AddADUSent(aduS);
        //                        //接收完成,通知客户端
        //                        AduReceivedDic[nameof(aduR.RequestAccepted)].Value = true;
        //                        AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Value = false;
        //                        MonitorNotifyDataChange(AduReceivedDic[nameof(aduR.RequestAccepted)].Id, new DataValue(AduReceivedDic[nameof(aduR.RequestAccepted)].Value));
        //                        MonitorNotifyDataChange(AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Id, new DataValue(AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Value));
        //                        Logger.Write("ADUSent新增到数据库,并成功通知客户端", category: Common.Utility.CategoryLog.Info);
        //                        break;
        //                    }

        //                    Thread.Sleep(_checkNewMessageInterval);
        //                    continue;
        //                }
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Logger.Write("检测ADUSent是否有新数据时,出现异常:" + ex.Message, category: Common.Utility.CategoryLog.Error);
        //        Logger.Write(ex, category: Common.Utility.CategoryLog.Error);
        //    }

        //    _checkNewMessageTimer.Change(0, _checkNewMessageInterval);
        //}

        private void NotifyClientServerFinished()
        {
            _notifyClientServerFinishedTimer.Change(Timeout.Infinite, Timeout.Infinite);
            try
            {
                var aduR = new ADUReceived();
                var currentVal = AduReceivedDic[nameof(aduR.DosingCompleted)].Value;
                if (currentVal != null && Convert.ToBoolean(currentVal))
                {
                    Logger.Write("ADUReceived正在反馈", category: Common.Utility.CategoryLog.Info);
                }
                else
                {
                    var ADUReceiveds = _dbHelper.GetUncompleteADUReceived();

                    if (ADUReceiveds != null && ADUReceiveds.Any())
                    {
                        foreach (var item in ADUReceiveds)
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

                            //通知并通知客户端,DosingCompleted=true
                            AduReceivedDic[nameof(item.DosingCompleted)].Value = true;
                            MonitorNotifyDataChange(AduReceivedDic[nameof(item.DosingCompleted)].Id, new DataValue(AduReceivedDic[nameof(item.DosingCompleted)].Value));

                            //等待客户端读取完成 ConsumptionAccepted=true
                            var aduS = new ADUSent();
                            object consumptionAcceptedValue = false;
                            do
                            {
                                consumptionAcceptedValue = AduSentDic[nameof(aduS.ConsumptionAccepted)].Value;
                                if (consumptionAcceptedValue == null || !Convert.ToBoolean(consumptionAcceptedValue))
                                {
                                    consumptionAcceptedValue = AduSentDic[nameof(aduS.ConsumptionAccepted)].Value;
                                    Thread.Sleep(300);
                                    continue;
                                }
                                else
                                {
                                    Logger.Write("客户端已确认收到收到ADUReceived消息", category: Common.Utility.CategoryLog.Info);

                                    AduReceivedDic[nameof(item.DosingCompleted)].Value = false;
                                    MonitorNotifyDataChange(AduReceivedDic[nameof(item.DosingCompleted)].Id, new DataValue(AduReceivedDic[nameof(item.DosingCompleted)].Value));
                                    Logger.Write("重置,开始下一轮ADUReceived,成功通知客户端", category: Common.Utility.CategoryLog.Info);

                                    item.IsComplete = true;
                                    //item.CompleteTime = DateTime.Now;
                                    _dbHelper.UpdateADUReceived(item);
                                    break;
                                }
                            } while (consumptionAcceptedValue == null || !Convert.ToBoolean(consumptionAcceptedValue));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Write("检测ADUReceived是否有新数据时,出现异常:" + ex.Message, category: Common.Utility.CategoryLog.Error);
                Logger.Write(ex, category: Common.Utility.CategoryLog.Error);
            }
            _notifyClientServerFinishedTimer.Change(1000, _checkNewMessageInterval);
        }

        private static bool CheckAduSendIsOk(ADUSent aduS)
        {
            //TODO:检测是否满足存库条件,根据实际情况完善
            return !string.IsNullOrEmpty(aduS.ProcessOrder);
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
            var prefix = "AduSend_";
            NodeId aduSendNodeId;
            NodeObject aduSend;
            NodeVariable processOrderNode, dateTimeNode, lotNode, plantCodeNode, deviceIdentifierNode, vesselIDNode, itemMaterialNode, batchStepIDNode, watchdogNode;

            #region add aduSend node
            aduSendNodeId = new NodeId(2, "AduSend");
            aduSend = new NodeObject(aduSendNodeId, new QualifiedName("AduSend"),
                new LocalizedText("AduSend"), new LocalizedText("AduSend Message Model"), (uint)(0xFFFFFFFF), (uint)(0xFFFFFFFF), 0);
            ItemsRoot.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), aduSend.Id, false));
            aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.ObjectsFolder), aduSend.Id, true));
            AddressSpaceTable.TryAdd(aduSend.Id, aduSend);
            #endregion

            var adu = new ADUSent();
            #region Add Sub node to AduSend
            var quantityNodel = new NodeVariable(new NodeId(2,
                prefix + "Quantity_Double"),
                        new QualifiedName("Quantity_Double"),
                              new LocalizedText("Quantity"),
                              new LocalizedText("Quantity"),
                              0, 0,
                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite  ,
                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite  ,
                              0, false,
                              new NodeId(UAConst.Double));

            //add Quantity to ItemsRoot
            aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), quantityNodel.Id, false));
            quantityNodel.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), quantityNodel.Id, true));
            AddressSpaceTable.TryAdd(quantityNodel.Id, quantityNodel);
            AduSentDic.Add(nameof(adu.Quantity), quantityNodel);

            processOrderNode = new NodeVariable(new NodeId(2, prefix + "ProcessOrder_String"), new QualifiedName("ProcessOrder_String"),
                                 new LocalizedText("ProcessOrder"),
                                 new LocalizedText("ProcessOrder_String"), 0, 0,
                                  AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                  AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                  0, false,
                                  new NodeId(UAConst.String));
            aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), processOrderNode.Id, false));
            processOrderNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), processOrderNode.Id, true));
            AddressSpaceTable.TryAdd(processOrderNode.Id, processOrderNode);
            AduSentDic.Add(nameof(adu.ProcessOrder), processOrderNode);

            dateTimeNode = new NodeVariable(new NodeId(2, prefix + "DateTime_DateTime"), new QualifiedName("DateTime_DateTime"),
                                                    new LocalizedText("DateTime"), new LocalizedText("DateTime_DateTime"), 0, 0,
                                                  AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                                  AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                                  1000, false,
                                                    new NodeId(UAConst.DateTime));
            aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), dateTimeNode.Id, false));
            dateTimeNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), dateTimeNode.Id, true));
            AddressSpaceTable.TryAdd(dateTimeNode.Id, dateTimeNode);
            AduSentDic.Add(nameof(adu.DateTime), dateTimeNode);

            var MaterialCodeNode = new NodeVariable(new NodeId(2, prefix + "MaterialCode_String"), new QualifiedName("MaterialCode_String"),
                                                                        new LocalizedText("MaterialCode"),
                                                                        new LocalizedText("MaterialCode_String"),
                                                                        0, 0,
                                                                      AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                                                      AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                                                      0, false,
                                                                     new NodeId(UAConst.String));

            aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), MaterialCodeNode.Id, false));
            MaterialCodeNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), MaterialCodeNode.Id, true));
            AddressSpaceTable.TryAdd(MaterialCodeNode.Id, MaterialCodeNode);
            AduSentDic.Add(nameof(adu.MaterialCode), MaterialCodeNode);

            var QuantityUOMNOde = new NodeVariable(new NodeId(2, prefix + "QuantityUOM_String"),
             new QualifiedName("QuantityUOM_String"), new LocalizedText("QuantityUOM"),
             new LocalizedText("QuantityUOM_String"), 0, 0,
             AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
             AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
             0, false,
             new NodeId(UAConst.String));
            aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), QuantityUOMNOde.Id, false));
            QuantityUOMNOde.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), QuantityUOMNOde.Id, true));
            AddressSpaceTable.TryAdd(QuantityUOMNOde.Id, QuantityUOMNOde);
            //add to AduSentDic
            AduSentDic.Add(nameof(adu.QuantityUOM), QuantityUOMNOde);


            lotNode = new NodeVariable(new NodeId(2, prefix + "Lot_Int32"),
                new QualifiedName("Lot_Int32"), new LocalizedText("Lot"),
                new LocalizedText("Lot_Int32"), 0, 0,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                0, false,
                new NodeId(UAConst.Int32));
            aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), lotNode.Id, false));
            lotNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), lotNode.Id, true));
            AddressSpaceTable.TryAdd(lotNode.Id, lotNode);
            //add to AduSentDic
            AduSentDic.Add(nameof(adu.Lot), lotNode);

            //PlantCode String ns=2;id=PlantCode_String
            plantCodeNode = new NodeVariable(new NodeId(2, prefix + "PlantCode_String"),
                new QualifiedName("PlantCode_String"), new LocalizedText("PlantCode"),
                new LocalizedText("PlantCode_String"), 0, 0,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                0, false,
                new NodeId(UAConst.String));
            aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), plantCodeNode.Id, false));
            plantCodeNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), plantCodeNode.Id, true));
            AddressSpaceTable.TryAdd(plantCodeNode.Id, plantCodeNode);
            AduSentDic.Add(nameof(adu.PlantCode), plantCodeNode);

            //DeviceIdentifier String ns=2;id=DeviceIdentifier_String
            deviceIdentifierNode = new NodeVariable(new NodeId(2, prefix + "DeviceIdentifier_String"),
                new QualifiedName("DeviceIdentifier_String"), new LocalizedText("DeviceIdentifier"),
                new LocalizedText("DeviceIdentifier_String"), 0, 0,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                0, false,
                new NodeId(UAConst.String));
            aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), deviceIdentifierNode.Id, false));
            deviceIdentifierNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), deviceIdentifierNode.Id, true));
            AddressSpaceTable.TryAdd(deviceIdentifierNode.Id, deviceIdentifierNode);
            AduSentDic.Add(nameof(adu.DeviceIdentifier), deviceIdentifierNode);

            //VesselID String ns=2;id=VesselID_String
            vesselIDNode = new NodeVariable(new NodeId(2, prefix + "VesselID_String"),
                new QualifiedName("VesselID_String"), new LocalizedText("VesselID"),
                new LocalizedText("VesselID_String"), 0, 0,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                0, false,
                new NodeId(UAConst.String));
            aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), vesselIDNode.Id, false));
            vesselIDNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), vesselIDNode.Id, true));
            AddressSpaceTable.TryAdd(vesselIDNode.Id, vesselIDNode);
            AduSentDic.Add(nameof(adu.VesselID), vesselIDNode);

            //ItemMaterial Int32 ns=2;id=ItemMaterial_Int32
            itemMaterialNode = new NodeVariable(new NodeId(2, prefix + "ItemMaterial_Int32"),
                new QualifiedName("ItemMaterial_Int32"), new LocalizedText("ItemMaterial"),
                new LocalizedText("ItemMaterial_Int32"), 0, 0,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                0, false,
                new NodeId(UAConst.Int32));
            aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), itemMaterialNode.Id, false));
            itemMaterialNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), itemMaterialNode.Id, true));
            AddressSpaceTable.TryAdd(itemMaterialNode.Id, itemMaterialNode);
            AduSentDic.Add(nameof(adu.ItemMaterial), itemMaterialNode);

            //BatchStepID Int32 ns=2;id=BatchStepI
            batchStepIDNode = new NodeVariable(new NodeId(2, prefix + "BatchStepID_Int32"),
                new QualifiedName("BatchStepID_Int32"), new LocalizedText("BatchStepID"),
                new LocalizedText("BatchStepID_Int32"), 0, 0,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                0, false,
                new NodeId(UAConst.Int32));
            aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), batchStepIDNode.Id, false));
            batchStepIDNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), batchStepIDNode.Id, true));
            AddressSpaceTable.TryAdd(batchStepIDNode.Id, batchStepIDNode);
            AduSentDic.Add(nameof(adu.BatchStepID), batchStepIDNode);

            //NewDosingRequest Boolean ns=2;id=NewDosingRequest_Boolean
            var newDosingRequestNode = new NodeVariable(new NodeId(2, prefix + "NewDosingRequest_Boolean"),
                new QualifiedName("NewDosingRequest_Boolean"), new LocalizedText("NewDosingRequest"),
                new LocalizedText("NewDosingRequest_Boolean"), 0, 0,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                0, false,
                new NodeId(UAConst.Boolean));
            newDosingRequestNode.Value = false;
            aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), newDosingRequestNode.Id, false));
            newDosingRequestNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), newDosingRequestNode.Id, true));
            AddressSpaceTable.TryAdd(newDosingRequestNode.Id, newDosingRequestNode);
            AduSentDic.Add(nameof(adu.NewDosingRequest), newDosingRequestNode);

            //ConsumptionAccepted Boolean ns=2;id=ConsumptionAccepted_Boolean
            var consumptionAcceptedNode = new NodeVariable(new NodeId(2, prefix + "ConsumptionAccepted_Boolean"),
                new QualifiedName("ConsumptionAccepted_Boolean"), new LocalizedText("ConsumptionAccepted"),
                new LocalizedText("ConsumptionAccepted_Boolean"), 0, 0,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                0, false,
                new NodeId(UAConst.Boolean));
            consumptionAcceptedNode.Value=false;
            aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), consumptionAcceptedNode.Id, false));
            consumptionAcceptedNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), consumptionAcceptedNode.Id, true));
            AddressSpaceTable.TryAdd(consumptionAcceptedNode.Id, consumptionAcceptedNode);
            AduSentDic.Add(nameof(adu.ConsumptionAccepted), consumptionAcceptedNode);

            //ProcessOrderDataSent Boolean ns=2;id=ProcessOrderDataSent_Boolean
            var processOrderDataSentNode = new NodeVariable(new NodeId(2, prefix + "ProcessOrderDataSent_Boolean"),
                new QualifiedName("ProcessOrderDataSent_Boolean"), new LocalizedText("ProcessOrderDataSent"),
                new LocalizedText("ProcessOrderDataSent_Boolean"), 0, 0,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                0, false,
                new NodeId(UAConst.Boolean));
            processOrderDataSentNode.Value=false;
            aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), processOrderDataSentNode.Id, false));
            processOrderDataSentNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), processOrderDataSentNode.Id, true));
            AddressSpaceTable.TryAdd(processOrderDataSentNode.Id, processOrderDataSentNode);
            AduSentDic.Add(nameof(adu.ProcessOrderDataSent), processOrderDataSentNode);

            //Watchdog Boolean ns=2;id=Watchdog_Boolean
            watchdogNode = new NodeVariable(new NodeId(2, prefix + "Watchdog_Boolean"),
                new QualifiedName("Watchdog_Boolean"), new LocalizedText("Watchdog"),
                new LocalizedText("Watchdog_Boolean"), 0, 0,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                0, false,
                new NodeId(UAConst.Boolean));
            aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), watchdogNode.Id, false));
            watchdogNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), watchdogNode.Id, true));
            AddressSpaceTable.TryAdd(watchdogNode.Id, watchdogNode);
            AduSentDic.Add(nameof(adu.Watchdog), watchdogNode);
            #endregion
        }

        private void AddAduReceived()
        {
            var prefix = "AduReceived_";

            #region add ADUReceived node
            var ADUReceivedNodeId = new NodeId(2, "ADUReceived");

            var ADUReceived = new NodeObject(ADUReceivedNodeId, new QualifiedName("ADUReceived"),
                new LocalizedText("AduReceived"), new LocalizedText("ADUReceived Message Model"), 0, 0, 0);

            ItemsRoot.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), ADUReceived.Id, false));
            ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.ObjectsFolder), ADUReceived.Id, true));
            AddressSpaceTable.TryAdd(ADUReceived.Id, ADUReceived);
            #endregion
            var adu = new ADUReceived();
            #region  add Sub Node to ADUReceived node

            var processOrderNode = new NodeVariable(new NodeId(2, prefix + "ProcessOrder_String"), new QualifiedName("ProcessOrder_String"),
                             new LocalizedText("ProcessOrder"), new LocalizedText("ProcessOrder_String"), 0, 0,
                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                              0, false,
                              new NodeId(UAConst.String));
            ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), processOrderNode.Id, false));
            processOrderNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), processOrderNode.Id, true));
            AddressSpaceTable.TryAdd(processOrderNode.Id, processOrderNode);
            AduReceivedDic.Add(nameof(adu.ProcessOrder), processOrderNode);

            var dateTimeNode = new NodeVariable(new NodeId(2, prefix + "DateTime_DateTime"), new QualifiedName("DateTime_DateTime"),
                                             new LocalizedText("DateTime"), new LocalizedText("DateTime_DateTime"), 0, 0,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              0, false,
                                              new NodeId(UAConst.DateTime));
            ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), dateTimeNode.Id, false));
            dateTimeNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), dateTimeNode.Id, true));
            AddressSpaceTable.TryAdd(dateTimeNode.Id, dateTimeNode);
            AduReceivedDic.Add(nameof(adu.DateTime), dateTimeNode);

            var materialCodeNode = new NodeVariable(new NodeId(2, prefix + "MaterialCode_String"), new QualifiedName("MaterialCode_String"),
                                             new LocalizedText("MaterialCode"), new LocalizedText("MaterialCode_String"), 0, 0,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              0, false,
                                              new NodeId(UAConst.String));
            ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), materialCodeNode.Id, false));
            materialCodeNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), materialCodeNode.Id, true));
            AddressSpaceTable.TryAdd(materialCodeNode.Id, materialCodeNode);
            AduReceivedDic.Add(nameof(adu.MaterialCode), materialCodeNode);

            var actualQuantityNode = new NodeVariable(new NodeId(2, prefix + "ActualQuantity_Double"), new QualifiedName("ActualQuantity_Double"),
                                             new LocalizedText("ActualQuantity"), new LocalizedText("ActualQuantity_Double"), 0, 0,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              0.0, false,
                                              new NodeId(UAConst.Double));
            ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), actualQuantityNode.Id, false));
            actualQuantityNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), actualQuantityNode.Id, true));
            AddressSpaceTable.TryAdd(actualQuantityNode.Id, actualQuantityNode);
            AduReceivedDic.Add(nameof(adu.ActualQuantity), actualQuantityNode);

            var quantityUOMNode = new NodeVariable(new NodeId(2, prefix + "QuantityUOM_String"), new QualifiedName("QuantityUOM_String"),
                                             new LocalizedText("QuantityUOM"), new LocalizedText("QuantityUOM_String"), 0, 0,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              0, false,
                                              new NodeId(UAConst.String));
            ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), quantityUOMNode.Id, false));
            quantityUOMNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), quantityUOMNode.Id, true));
            AddressSpaceTable.TryAdd(quantityUOMNode.Id, quantityUOMNode);
            AduReceivedDic.Add(nameof(adu.QuantityUOM), quantityUOMNode);

            var lotNode = new NodeVariable(new NodeId(2, prefix + "Lot_Int32"), new QualifiedName("Lot_Int32"),
                                             new LocalizedText("Lot"), new LocalizedText("Lot_Int32"), 0, 0,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              0, false,
                                              new NodeId(UAConst.Int32));
            ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), lotNode.Id, false));
            lotNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), lotNode.Id, true));
            AddressSpaceTable.TryAdd(lotNode.Id, lotNode);
            AduReceivedDic.Add(nameof(adu.Lot), lotNode);

            var plantCodeNode = new NodeVariable(new NodeId(2, prefix + "PlantCode_String"), new QualifiedName("PlantCode_String"),
                                             new LocalizedText("PlantCode"), new LocalizedText("PlantCode_String"), 0, 0,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              0, false,
                                              new NodeId(UAConst.String));
            ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), plantCodeNode.Id, false));
            plantCodeNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), plantCodeNode.Id, true));
            AddressSpaceTable.TryAdd(plantCodeNode.Id, plantCodeNode);
            AduReceivedDic.Add(nameof(adu.PlantCode), plantCodeNode);

            var deviceIdentifierNode = new NodeVariable(new NodeId(2, prefix + "DeviceIdentifier_String"), new QualifiedName("DeviceIdentifier_String"),
                                             new LocalizedText("DeviceIdentifier"), new LocalizedText("DeviceIdentifier_String"), 0, 0,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              0, false,
                                              new NodeId(UAConst.String));
            ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), deviceIdentifierNode.Id, false));
            deviceIdentifierNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), deviceIdentifierNode.Id, true));
            AddressSpaceTable.TryAdd(deviceIdentifierNode.Id, deviceIdentifierNode);
            AduReceivedDic.Add(nameof(adu.DeviceIdentifier), deviceIdentifierNode);


            var vesselIDNode = new NodeVariable(new NodeId(2, prefix + "VesselID_String"), new QualifiedName("VesselID_String"),
                                             new LocalizedText("VesselID"), new LocalizedText("VesselID_String"), 0, 0,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              0, false,
                                              new NodeId(UAConst.String));
            ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), vesselIDNode.Id, false));
            vesselIDNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), vesselIDNode.Id, true));
            AddressSpaceTable.TryAdd(vesselIDNode.Id, vesselIDNode);
            AduReceivedDic.Add(nameof(adu.VesselID), vesselIDNode);

            var itemMaterialNode = new NodeVariable(new NodeId(2, prefix + "ItemMaterial_Int32"), new QualifiedName("ItemMaterial_Int32"),
                                             new LocalizedText("ItemMaterial"), new LocalizedText("ItemMaterial_Int32"), 0, 0,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              0, false,
                                              new NodeId(UAConst.Int32));
            ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), itemMaterialNode.Id, false));
            itemMaterialNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), itemMaterialNode.Id, true));
            AddressSpaceTable.TryAdd(itemMaterialNode.Id, itemMaterialNode);
            AduReceivedDic.Add(nameof(adu.ItemMaterial), itemMaterialNode);


            var batchStepIDNode = new NodeVariable(new NodeId(2, prefix + "BatchStepID_Int32"), new QualifiedName("BatchStepID_Int32"),
                                             new LocalizedText("BatchStepID"), new LocalizedText("BatchStepID_Int32"), 0, 0,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              0, false,
                                              new NodeId(UAConst.Int32));
            ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), batchStepIDNode.Id, false));
            batchStepIDNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), batchStepIDNode.Id, true));
            AddressSpaceTable.TryAdd(batchStepIDNode.Id, batchStepIDNode);
            AduReceivedDic.Add(nameof(adu.BatchStepID), batchStepIDNode);

            var readyForNewDosingNode = new NodeVariable(new NodeId(2, prefix + "ReadyForNewDosing_Boolean"), new QualifiedName("ReadyForNewDosing_Boolean"),
                                             new LocalizedText("ReadyForNewDosing"), new LocalizedText("ReadyForNewDosing_Boolean"), 0, 0,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              0, false,
                                              new NodeId(UAConst.Boolean));
            readyForNewDosingNode.Value=false;
            ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), readyForNewDosingNode.Id, false));
            readyForNewDosingNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), readyForNewDosingNode.Id, true));
            AddressSpaceTable.TryAdd(readyForNewDosingNode.Id, readyForNewDosingNode);
            AduReceivedDic.Add(nameof(adu.ReadyForNewDosing), readyForNewDosingNode);

            var dosingCompletedNode = new NodeVariable(new NodeId(2, prefix + "DosingCompleted_Boolean"), new QualifiedName("DosingCompleted_Boolean"),
                                             new LocalizedText("DosingCompleted"), new LocalizedText("DosingCompleted_Boolean"), 0, 0,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              0, false,
                                              new NodeId(UAConst.Boolean));
            dosingCompletedNode.Value=false;
            ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), dosingCompletedNode.Id, false));
            dosingCompletedNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), dosingCompletedNode.Id, true));
            AddressSpaceTable.TryAdd(dosingCompletedNode.Id, dosingCompletedNode);
            AduReceivedDic.Add(nameof(adu.DosingCompleted), dosingCompletedNode);

            var requestAcceptedNode = new NodeVariable(new NodeId(2, prefix + "RequestAccepted_Boolean"), new QualifiedName("RequestAccepted_Boolean"),
                                             new LocalizedText("RequestAccepted"), new LocalizedText("RequestAccepted_Boolean"), 0, 0,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              0, false,
                                              new NodeId(UAConst.Boolean));
            requestAcceptedNode.Value=true;
            ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), requestAcceptedNode.Id, false));
            requestAcceptedNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), requestAcceptedNode.Id, true));
            AddressSpaceTable.TryAdd(requestAcceptedNode.Id, requestAcceptedNode);
            AduReceivedDic.Add(nameof(adu.RequestAccepted), requestAcceptedNode);

            var watchdogNode = new NodeVariable(new NodeId(2, prefix + "Watchdog_Boolean"), new QualifiedName("Watchdog_Boolean"),
                                             new LocalizedText("Watchdog"), new LocalizedText("Watchdog_Boolean"), 0, 0,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              AccessLevel.CurrentRead | AccessLevel.CurrentWrite,
                                              0, false,
                                              new NodeId(UAConst.Boolean));
            ADUReceived.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), watchdogNode.Id, false));
            watchdogNode.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), watchdogNode.Id, true));
            AddressSpaceTable.TryAdd(watchdogNode.Id, watchdogNode);
            AduReceivedDic.Add(nameof(adu.Watchdog), watchdogNode);

            #endregion
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
                                    AduReceivedDic[nameof(aduR.RequestAccepted)].Value = true;
                                    //AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Value = false;
                                    MonitorNotifyDataChange(AduReceivedDic[nameof(aduR.RequestAccepted)].Id, new DataValue(AduReceivedDic[nameof(aduR.RequestAccepted)].Value));
                                    //MonitorNotifyDataChange(AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Id, new DataValue(AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Value));
                                    Logger.Write("ADUSent新增数据不合规范:" + JsonConvert.SerializeObject(aduS), category: Common.Utility.CategoryLog.Error);
                                    Logger.Write("ADUSent新增数据未添加到数据库,成功通知客户端", category: Common.Utility.CategoryLog.Info);
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

        /// <summary>
        /// 浏览某个节点
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        protected override DataValue HandleReadRequestInternal(NodeId id)
        {
            Node node = null;
            if (id.NamespaceIndex == 2 &&
                AddressSpaceTable.TryGetValue(id, out node))
            {
                //if (node == Node1D)
                //{
                //    return new DataValue(new float[] { 1.0f, 2.0f, 3.0f }, StatusCode.Good, DateTime.Now);
                //}
                //else if (node == Node2D)
                //{
                //    return new DataValue(new float[2, 2]
                //    {
                //        { 1.0f, 2.0f },
                //        { 3.0f, 4.0f }
                //    }, StatusCode.Good, DateTime.Now);
                //}
                //else
                //{
                //    return new DataValue(3.14159265, StatusCode.Good, DateTime.Now);
                //}
                if (node.BrowseName.Name.EndsWith("Boolean"))
                {
                    if (node is NodeVariable nodeVar && nodeVar.Value != null)
                    {
                        return new DataValue(nodeVar.Value, StatusCode.Good, DateTime.Now);
                    }
                    return new DataValue(false, StatusCode.Good, DateTime.Now);
                }
                else if (node.BrowseName.Name.EndsWith("String"))
                {
                    if (node is NodeVariable nodeVar && nodeVar.Value != null)
                    {
                        return new DataValue(nodeVar.Value, StatusCode.Good, DateTime.Now);
                    }
                    return new DataValue("", StatusCode.Good, DateTime.Now);
                }
                else if (node.BrowseName.Name.EndsWith("Double"))
                {
                    if (node is NodeVariable nodeVar && nodeVar.Value != null)
                    {
                        return new DataValue(nodeVar.Value, StatusCode.Good, DateTime.Now);
                    }
                    return new DataValue(0d, StatusCode.Good, DateTime.Now);
                }
                else if (node.BrowseName.Name.EndsWith("DateTime"))
                {
                    if (node is NodeVariable nodeVar && nodeVar.Value != null)
                    {
                        return new DataValue(nodeVar.Value, StatusCode.Good, DateTime.Now);
                    }
                    return new DataValue(new DateTime(1900,1,1), StatusCode.Good, DateTime.Now);
                }
                else if (node.BrowseName.Name.EndsWith("Int32"))
                {
                    if (node is NodeVariable nodeVar && nodeVar.Value != null)
                    {
                        return new DataValue(nodeVar.Value, StatusCode.Good, DateTime.Now);
                    }
                    return new DataValue(0, StatusCode.Good, DateTime.Now);
                }
            }

            return base.HandleReadRequestInternal(id);
        }

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
