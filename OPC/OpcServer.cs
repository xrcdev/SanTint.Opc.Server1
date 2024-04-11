
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
        /// <summary>
        /// 小数位数配置
        /// </summary>
        private static int _precision = 3;
        static DBHelper _dbHelper = new DBHelper();

        NodeObject ItemsRoot = default;
        public ConcurrentDictionary<NodeId, Node> MyAddressSpaceTable;
        Dictionary<string, NodeVariable> _aduSentDic = new Dictionary<string, NodeVariable>();
        Dictionary<string, NodeVariable> _aduReceivedDic = new Dictionary<string, NodeVariable>();
        private object _sendLocker = new object();
        private object _receivedLocker = new object();
        Dictionary<Type, UAConst> _typeMapper = new Dictionary<Type, UAConst>()
        {
            { typeof(string),UAConst.String },
            { typeof(double),UAConst.Double },
            { typeof(float),UAConst.Float },
            { typeof(int),UAConst.Int32 },
            { typeof(bool),UAConst.Boolean },
            { typeof(DateTime),UAConst.DateTime }
        };
        ApplicationDescription _uaAppDesc;
        X509Certificate2 _appCertificate = null;
        RSACryptoServiceProvider _cryptPrivateKey = null;
        public override X509Certificate2 ApplicationCertificate
        {
            get { return _appCertificate; }
        }

        public override RSACryptoServiceProvider ApplicationPrivateKey
        {
            get { return _cryptPrivateKey; }
        }
        Thread feedbackThread = null;
        /// <summary>
        /// 初始化节点
        /// </summary>
        public OpcServer()
        {
            #region 初始化配置
            System.Configuration.Configuration config = System.Configuration.ConfigurationManager.OpenExeConfiguration(System.Configuration.ConfigurationUserLevel.None);
            var precision = config.AppSettings.Settings["Precision"].Value;
            if (!string.IsNullOrWhiteSpace(precision))
            {
                var tempPrecision = 0;
                if (int.TryParse(precision, out tempPrecision))
                {
                    _precision = tempPrecision;
                    if (_precision < 2)
                    {
                        _precision = 3;
                    }
                }
            }
            #endregion

            LoadCertificateAndPrivateKey();
            _uaAppDesc = new ApplicationDescription(
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
            if (feedbackThread == null)
            {
                feedbackThread = new Thread(NotifyClientServerFinished);
                feedbackThread.Name = "feedbackThread";
                feedbackThread.IsBackground = true;
                feedbackThread.Start();
            }
            //Task.Run(() => NotifyClientServerFinished());
            //_notifyClientServerFinishedTimer = new Timer((obj) => NotifyClientServerFinished(), null, _checkNewMessageInterval, _checkNewMessageInterval);
        }

        #region 私有方法

        private void NotifyClientServerFinished()
        {
            while (true)
            {
                Thread.Sleep(1000);
                try
                {
                    var aduS = new ADUSent();
                    var aduR = new ADUReceived();

                    #region 写数据前,需要ConsumptionAccepted=false;
                    var currentConsumptionAcceptedVal = _aduSentDic[nameof(aduS.ConsumptionAccepted)].Value;
                    bool isConsumptionAccepted = false;
                    //异常数据纠正
                    if (currentConsumptionAcceptedVal == null || !bool.TryParse(currentConsumptionAcceptedVal.ToString(), out isConsumptionAccepted))
                    {
                        Logger.Write("Read ConsumptionAccepted value:" + currentConsumptionAcceptedVal?.ToString(), category: Common.Utility.CategoryLog.Error);
                        Logger.Write("correct ConsumptionAccepted value to false.", category: Common.Utility.CategoryLog.Error);
                        _aduReceivedDic[nameof(aduS.ConsumptionAccepted)].Value = false;
                        OutputDataToLog();
                    }
                    if (isConsumptionAccepted) continue;
                    #endregion


                    var ADUReceiveds = _dbHelper.GetUncompleteADUReceived();
                    if (ADUReceiveds != null && ADUReceiveds.Any())
                    {
                        foreach (ADUReceived item in ADUReceiveds)
                        {
                            //设置节点值
                            _aduReceivedDic[nameof(item.ProcessOrder)].Value = item.ProcessOrder;
                            SetSingleDataTag(_aduReceivedDic[nameof(item.ProcessOrder)]);
                            _aduReceivedDic[nameof(item.DateTime)].Value = item.DateTime;
                            SetSingleDataTag(_aduReceivedDic[nameof(item.DateTime)]);
                            _aduReceivedDic[nameof(item.MaterialCode)].Value = item.MaterialCode;
                            SetSingleDataTag(_aduReceivedDic[nameof(item.MaterialCode)]);
                            _aduReceivedDic[nameof(item.ActualQuantity)].Value = (float)Math.Round(item.ActualQuantity, _precision);
                            _aduReceivedDic[nameof(item.QuantityUOM)].Value = item.QuantityUOM;
                            _aduReceivedDic[nameof(item.Lot)].Value = item.Lot;
                            _aduReceivedDic[nameof(item.PlantCode)].Value = item.PlantCode;
                            _aduReceivedDic[nameof(item.DeviceIdentifier)].Value = item.DeviceIdentifier;
                            _aduReceivedDic[nameof(item.VesselID)].Value = item.VesselID;
                            _aduReceivedDic[nameof(item.ItemMaterial)].Value = item.ItemMaterial;
                            _aduReceivedDic[nameof(item.BatchStepID)].Value = item.BatchStepID;
                            _aduReceivedDic[nameof(item.Watchdog)].Value = item.Watchdog;
                            //通知客户端,DosingCompleted=true
                            _aduReceivedDic[nameof(item.DosingCompleted)].Value = true;


                            SetSingleDataTag(_aduReceivedDic[nameof(item.ActualQuantity)]);
                            SetSingleDataTag(_aduReceivedDic[nameof(item.QuantityUOM)]);
                            SetSingleDataTag(_aduReceivedDic[nameof(item.Lot)]);
                            SetSingleDataTag(_aduReceivedDic[nameof(item.PlantCode)]);
                            SetSingleDataTag(_aduReceivedDic[nameof(item.DeviceIdentifier)]);
                            SetSingleDataTag(_aduReceivedDic[nameof(item.VesselID)]);
                            SetSingleDataTag(_aduReceivedDic[nameof(item.ItemMaterial)]);
                            SetSingleDataTag(_aduReceivedDic[nameof(item.BatchStepID)]);
                            SetSingleDataTag(_aduReceivedDic[nameof(item.Watchdog)]);
                            SetSingleDataTag(_aduReceivedDic[nameof(item.DosingCompleted)]);


                            MonitorNotifyDataChange(_aduReceivedDic[nameof(item.DosingCompleted)].Id, new DataValue(_aduReceivedDic[nameof(item.DosingCompleted)].Value));

                            //等待客户端读取完成 ConsumptionAccepted=true
                            object consumptionAcceptedValue = false;
                            int tryTime = 0;
                            while (true)
                            {
                                currentConsumptionAcceptedVal = _aduSentDic[nameof(aduS.ConsumptionAccepted)].Value;
                                isConsumptionAccepted = false;
                                //异常数据纠正
                                if (currentConsumptionAcceptedVal == null || !bool.TryParse(currentConsumptionAcceptedVal.ToString(), out isConsumptionAccepted))
                                {
                                    Logger.Write("Read ConsumptionAccepted value:" + currentConsumptionAcceptedVal?.ToString(), category: Common.Utility.CategoryLog.Error);
                                    Logger.Write("correct ConsumptionAccepted value to false", category: Common.Utility.CategoryLog.Error);
                                    OutputDataToLog();
                                    _aduReceivedDic[nameof(aduS.ConsumptionAccepted)].Value = false;
                                    isConsumptionAccepted = false;
                                }

                                if (isConsumptionAccepted == false)
                                {
                                    tryTime += 1;
                                    Thread.Sleep(1000);
                                    if (tryTime > 0 && tryTime % 20 == 0)
                                        Logger.Write($"等待{tryTime.ToString()}秒,未收到客户端的ConsumptionAccepted=1的消息 ", category: Common.Utility.CategoryLog.Error);
                                    continue;
                                }

                                //put a delay of 10 seconds (1) after they have populated the consumption record, to ensure it sync across
                                Logger.Write("【已收到】OPC Server Received  ConsumptionAccepted=1 ", category: Common.Utility.CategoryLog.Error);
                                //Thread.Sleep(10000);
                                Logger.Write("Set DosingCompleted=false and notify OPC client ", category: Common.Utility.CategoryLog.Error);
                                _aduReceivedDic[nameof(item.DosingCompleted)].Value = false;
                                MonitorNotifyDataChange(_aduReceivedDic[nameof(item.DosingCompleted)].Id, new DataValue(_aduReceivedDic[nameof(item.DosingCompleted)].Value));
                                Logger.Write("Notify client successfully, start next round", category: Common.Utility.CategoryLog.Error);

                                item.IsComplete = true;
                                _dbHelper.UpdateADUReceived(item);
                                tryTime = 0;
                                break;
                            }
                        }
                    }

                }
                catch (Exception ex)
                {
                    try
                    {
                        Logger.Write("检测ADUReceived是否有新数据时,出现异常:" + ex.Message, category: Common.Utility.CategoryLog.Error);
                        Logger.Write(ex, category: Common.Utility.CategoryLog.Error);
                    }
                    catch (Exception) {}
                }
            }

            //_notifyClientServerFinishedTimer.Change(1000, _checkNewMessageInterval);
        }

        private void SetSingleDataTag(NodeVariable nodeVariable)
        {
            MonitorNotifyDataChange(nodeVariable.Id, new DataValue(nodeVariable.Value));
        }

        private void OutputDataToLog()
        {
            Logger.Write("Abnormal data detected.", category: Common.Utility.CategoryLog.Error);
            Logger.Write("output values in aduSent.", category: Common.Utility.CategoryLog.Error);
            foreach (var item in _aduSentDic)
            {
                Logger.Write($"{item.Key} => {item.Value?.Value ?? string.Empty}", category: Common.Utility.CategoryLog.Error);
            }

            Logger.Write("output values in aduReceived.", category: Common.Utility.CategoryLog.Error);
            foreach (var item in _aduReceivedDic)
            {
                Logger.Write($"{item.Key} => {item.Value?.Value ?? string.Empty}", category: Common.Utility.CategoryLog.Error);
            }
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
                var processOrder = _aduSentDic[nameof(aduS.ProcessOrder)].Value;
                if (processOrder != null)
                {
                    aduS.ProcessOrder = processOrder.ToString();
                }
                var dateTime = _aduSentDic[nameof(aduS.DateTime)].Value;
                if (dateTime != null)
                {
                    aduS.DateTime = dateTime.ToString();
                }
                //add MaterialCode
                var materialCode = _aduSentDic[nameof(aduS.MaterialCode)].Value;
                if (materialCode != null)
                {
                    aduS.MaterialCode = materialCode.ToString();
                }
                //Quantity
                var quantity = _aduSentDic[nameof(aduS.Quantity)].Value;
                if (quantity != null)
                {
                    aduS.Quantity = (float)Math.Round(Convert.ToSingle(quantity), _precision);
                }
                //QuantityUOM
                var quantityUOM = _aduSentDic[nameof(aduS.QuantityUOM)].Value;
                if (quantityUOM != null)
                {
                    aduS.QuantityUOM = quantityUOM.ToString();
                }

                var lot = _aduSentDic[nameof(aduS.Lot)].Value;
                if (lot != null)
                {
                    aduS.Lot = lot.ToString();
                }
                //add PlantCode
                var plantCode = _aduSentDic[nameof(aduS.PlantCode)].Value;
                if (plantCode != null)
                {
                    aduS.PlantCode = plantCode.ToString();
                }

                var deviceIdentifier = _aduSentDic[nameof(aduS.DeviceIdentifier)].Value;
                if (deviceIdentifier != null)
                {
                    aduS.DeviceIdentifier = deviceIdentifier.ToString();
                }
                // add VesselID
                var vesselID = _aduSentDic[nameof(aduS.VesselID)].Value;
                if (vesselID != null)
                {
                    aduS.VesselID = vesselID.ToString();
                }
                //add ItemMaterial
                var itemMaterial = _aduSentDic[nameof(aduS.ItemMaterial)].Value;
                if (itemMaterial != null)
                {
                    aduS.ItemMaterial = Convert.ToInt32(itemMaterial);
                }
                //add BatchStepID
                var batchStepID = _aduSentDic[nameof(aduS.BatchStepID)].Value;
                if (batchStepID != null)
                {
                    aduS.BatchStepID = Convert.ToInt32(batchStepID);
                }

                var watchdog = _aduSentDic[nameof(aduS.Watchdog)].Value;
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
            var excludes = "ID,DataIdentifier,IsSTComplete".Split(",".ToCharArray(), options: StringSplitOptions.RemoveEmptyEntries);
            var properties = adu.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var property in properties)
            {
                var nodeName = property.Name;
                if (excludes.Contains(nodeName)) continue;
                AddReadWriteDataNode(this._aduSentDic, prefix, aduSend, property.PropertyType, nodeName);
            }
        }


        private void AddReadWriteDataNode(Dictionary<string, NodeVariable> nodeDic, string prefix, NodeObject aduSend, Type nodeType, string nodeName)
        {
            NodeVariable nVar = new NodeVariable(new NodeId(2, prefix + nodeName.WrapQuote()), new QualifiedName(nodeName), new LocalizedText(nodeName), new LocalizedText(nodeName), 0U, 0U, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, AccessLevel.CurrentRead | AccessLevel.CurrentWrite, 0.0, false, new NodeId(_typeMapper[nodeType]), ValueRank.Scalar);
            if (nodeType == typeof(bool))
                nVar.Value = nodeName == "RequestAccepted";
            aduSend.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), nVar.Id, false));
            nVar.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), nVar.Id, true));
            this.AddressSpaceTable.TryAdd(nVar.Id, nVar);
            nodeDic.Add(nodeName, nVar);
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
            var excludes = "ID,Quantity,DataIdentifier,IsSTComplete".Split(",".ToCharArray(), options: StringSplitOptions.RemoveEmptyEntries);
            var properties = adu.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var property in properties)
            {
                var nodeName = property.Name;
                if (excludes.Contains(nodeName)) continue;
                AddReadWriteDataNode(this._aduReceivedDic, prefix, ADUReceived, property.PropertyType, nodeName);
            }
        }
        private void LoadCertificateAndPrivateKey()
        {
            try
            {
                // Try to load existing (public key) and associated private key
                _appCertificate = new X509Certificate2("ServerCert.der");
                _cryptPrivateKey = new RSACryptoServiceProvider();

                var rsaPrivParams = UASecurity.ImportRSAPrivateKey(File.ReadAllText("ServerKey.pem"));
                _cryptPrivateKey.ImportParameters(rsaPrivParams);
            }
            catch (Exception ex)
            {
                try
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

                    _appCertificate = cngKey.CreateSelfSignedCertificate(certParams);

                    var certPrivateCNG = new RSACng(_appCertificate.GetCngPrivateKey());
                    var certPrivateParams = certPrivateCNG.ExportParameters(true);

                    File.WriteAllText("ServerCert.der", UASecurity.ExportPEM(_appCertificate));
                    File.WriteAllText("ServerKey.pem", UASecurity.ExportRSAPrivateKey(certPrivateParams));

                    _cryptPrivateKey = new RSACryptoServiceProvider();
                    _cryptPrivateKey.ImportParameters(certPrivateParams);
                }
                catch (Exception ex1)
                {

                }
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
            if (id.NamespaceIndex == 2 && this.AddressSpaceTable.TryGetValue(id, out Node node))
            {
                NodeVariable nodeVariable = node as NodeVariable;
                if (nodeVariable != null && nodeVariable.Value == null)
                {
                    nodeVariable.Value = GetDefaultValue(nodeVariable.DataType.NumericIdentifier);
                }

                try
                {
                    return new DataValue(nodeVariable.Value, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                }
                catch (Exception ex)
                {
                    Logger.Write("MonitorNotifyDataChange出现异常:" + ex.Message, Common.Utility.CategoryLog.Error);
                }
            }
            return base.HandleReadRequestInternal(id);
        }

        private DataValue GetDefaultValue(uint dataType)
        {
            switch (dataType)
            {
                case 1:
                    return new DataValue(false, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                case 2:
                case 3:
                case 4:
                case 5:
                case 6:
                case 7:
                    return new DataValue(0, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                case 8:
                case 9:
                    return new DataValue(0L, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                case 10:
                    return new DataValue(0f, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                case 11:
                    return new DataValue(0.0, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                case 12:
                    return new DataValue(string.Empty, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                case 13:
                    return new DataValue(new DateTime(1900, 1, 1), new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                case 14:
                    return new DataValue(Guid.NewGuid(), new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                case 15:
                    return new DataValue(new byte[0], new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                case 16:
                    return new DataValue(new XElement("element"), new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                case 19:
                    return new DataValue(StatusCode.Good, new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                case 20:
                    return new DataValue(default(QualifiedName), new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                case 21:
                    return new DataValue(new LocalizedText(""), new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                case 22:
                    return new DataValue(new ExtensionObject(), new StatusCode?(StatusCode.Good), new DateTime?(DateTime.Now), null);
                default:
                    return null;
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
                                _aduReceivedDic[nameof(aduR.RequestAccepted)].Value = false;
                                _aduReceivedDic[nameof(aduR.ReadyForNewDosing)].Value = true;
                                MonitorNotifyDataChange(_aduReceivedDic[nameof(aduR.RequestAccepted)].Id, new DataValue(_aduReceivedDic[nameof(aduR.RequestAccepted)].Value));
                                MonitorNotifyDataChange(_aduReceivedDic[nameof(aduR.ReadyForNewDosing)].Id, new DataValue(_aduReceivedDic[nameof(aduR.ReadyForNewDosing)].Value));
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
                                    DBHelper.CheckOrInitSqliteDb();
                                    _dbHelper.AddADUSent(aduS);

                                    //接收完成,通知客户端
                                    _aduReceivedDic[nameof(aduR.RequestAccepted)].Value = true;
                                    //AduReceivedDic[nameof(aduR.ReadyForNewDosing)].Value = false;
                                    MonitorNotifyDataChange(_aduReceivedDic[nameof(aduR.RequestAccepted)].Id, new DataValue(true));
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
                    Logger.Write("HandleWriteRequest,出现错误:" + ex.Message, Common.Utility.CategoryLog.Error);
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
            string[] discoveryUrls = _uaAppDesc.DiscoveryUrls;
            if (discoveryUrls == null && !string.IsNullOrEmpty(endpointUrlHint))
            {
                discoveryUrls = new string[] { endpointUrlHint };
            }

            return new ApplicationDescription(_uaAppDesc.ApplicationUri, _uaAppDesc.ProductUri, _uaAppDesc.ApplicationName,
                _uaAppDesc.Type, _uaAppDesc.GatewayServerUri, _uaAppDesc.DiscoveryProfileUri, discoveryUrls);
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
            if (Level == LogLevel.Error)
            {
#if DEBUG
                Console.WriteLine("OpcServer:[{0}] {1}", Level.ToString(), Str);
#endif
                Logger.Write(string.Format("OpcServer:[{0}] {1}", Level.ToString(), Str), Common.Utility.CategoryLog.Warn);
            }
        }
    }
}
