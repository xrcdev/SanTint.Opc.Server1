
using LibUA;
using LibUA.Core;

using OPCUAServer;

using SanTint.Common.Log;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http.SelfHost;

namespace SanTint.Opc.Server
{
    public class OpcServerApp
    {
        LibUA.Server.Master server = default;
        public void Start()
        {
            SanTint.Common.Log.Logger.Write("OpcServer即将启动");
            OpcServer _app = new OpcServer();
            System.Configuration.Configuration config = System.Configuration.ConfigurationManager.OpenExeConfiguration(System.Configuration.ConfigurationUserLevel.None);
            var strIP = config.AppSettings.Settings["OpcIp"].Value;
            var strPort = config.AppSettings.Settings["OpcPort"].Value;
            if (string.IsNullOrWhiteSpace(strPort)) strPort = "8089";
            else strPort = strPort.Trim();
            server = new LibUA.Server.Master(_app, Convert.ToInt32(strPort), 10, 30, 100, new OpcConsoleLogger());
            server.Start();
            SanTint.Common.Log.Logger.Write("OpcServer启动成功");
        }

        public void Stop()
        {
            SanTint.Common.Log.Logger.Write("OpcServer即将关闭");
            server.Stop();
            SanTint.Common.Log.Logger.Write("OpcServer停止成功");
        }
    }

    public class OpcServer : LibUA.Server.Application
    {
        public OpcServer()
        {
            ApplicationDescription uaAppDesc = new ApplicationDescription(
                "urn:SanTintOpcServer",
                "http://opcfoundation.org/Server",
                new LocalizedText("en-US", "SanTint OPC UA Server"),
                ApplicationType.Server,
                null,
                null,
                null);

            var rootNodeId = new NodeId(2, "DataBlocksGlobal");
            NodeObject ItemsRoot = new NodeObject(rootNodeId, new QualifiedName("DataBlocksGlobal"), new LocalizedText("DataBlocksGlobal"),
                     new LocalizedText("ADU Sent Infomation Model"), 0, 0, 0);
            
            AddressSpaceTable[new NodeId(UAConst.ObjectsFolder)]
                .References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), rootNodeId, false));

            ItemsRoot.References.Add(
                new ReferenceNode(new NodeId(UAConst.Organizes), new NodeId(UAConst.ObjectsFolder), true));

            AddressSpaceTable.TryAdd(ItemsRoot.Id, ItemsRoot);

            //var nodeTypeFloat = new NodeId(0, 10);
            //var Node1D = new NodeVariable(new NodeId(2, (uint)(1000 + 1)), new QualifiedName("Array - 1D"),
            //             new LocalizedText("Array - 1D"), new LocalizedText("Array - 1D"), 0, 0,
            //             AccessLevel.CurrentRead, AccessLevel.CurrentRead, 0, false, nodeTypeFloat, ValueRank.OneDimension);
            //ItemsRoot.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), Node1D.Id, false));
            //Node1D.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), ItemsRoot.Id, true));
            //AddressSpaceTable.TryAdd(Node1D.Id, Node1D);


            //var ItemsRoot = AddressSpaceTable[new NodeId(UAConst.ObjectsFolder)] as NodeObject;

            //var ProcessOrder_node = new NodeVariable(new NodeId(2, (uint)(1000 + 1)), new QualifiedName("ProcessOrder"),
            //  new LocalizedText("Process Order"), new LocalizedText("Process Order"), 0, 0,
            //  AccessLevel.CurrentRead, AccessLevel.CurrentRead,0, false, VariantType.String, ValueRanks.Scalar);
            //ItemsRoot.AddChild(ProcessOrder_node);

            //var DateTime_node = new NodeVariable(new NodeId(2, (uint)(1000 + 2)), new QualifiedName("DateTime"),
            //  new LocalizedText("Date Time"), new LocalizedText("Date Time"), 0, 0,
            //  AccessLevel.CurrentRead, AccessLevel.CurrentRead,0, false, DataTypeIds.DateTime, ValueRanks.Scalar);
            //ItemsRoot.AddChild(DateTime_node);

            //var MaterialCode_node = new NodeVariable(new NodeId(2, (uint)(1000 + 3)), new QualifiedName("MaterialCode"),
            //  new LocalizedText("Material Code"), new LocalizedText("Material Code"), 0, 0,
            //  AccessLevel.CurrentRead, AccessLevel.CurrentRead,0, false, DataTypeIds.Int32, ValueRanks.Scalar);
            //ItemsRoot.AddChild(MaterialCode_node);

            //var Quantity_node = new NodeVariable(new NodeId(2, (uint)(1000 + 4)), new QualifiedName("Quantity"),
            //  new LocalizedText("Quantity"), new LocalizedText("Quantity"), 0, 0,
            //  AccessLevel.CurrentRead, AccessLevel.CurrentRead,0, false, DataTypeIds.Double, ValueRanks.Scalar);
            //ItemsRoot.AddChild(Quantity_node);

            //var QuantityUOM_node = new NodeVariable(new NodeId(2, (uint)(1000 + 5)), new QualifiedName("QuantityUOM"),
            //  new LocalizedText("Quantity UOM"), new LocalizedText("Quantity UOM"), 0, 0,
            //  AccessLevel.CurrentRead, AccessLevel.CurrentRead,0, false, DataTypeIds.String, ValueRanks.Scalar);
            //ItemsRoot.AddChild(QuantityUOM_node);

            //var Lot_node = new NodeVariable(new NodeId(2, (uint)(1000 + 6)), new QualifiedName("Lot"),
            //  new LocalizedText("Lot"), new LocalizedText("Lot"), 0, 0,
            //  AccessLevel.CurrentRead, AccessLevel.CurrentRead,0, false, DataTypeIds.Int32, ValueRanks.Scalar);
            //ItemsRoot.AddChild(Lot_node);

            //var Node2D = new NodeVariable(new NodeId(2, (uint)(1000 + 2)), new QualifiedName("Array - 2D"),
            //            new LocalizedText("Array - 2D"), new LocalizedText("Array - 2D"), 0, 0,
            //            AccessLevel.CurrentRead, AccessLevel.CurrentRead, 0, false, nodeTypeFloat, ValueRank.OneOrMoreDimensions);
            //ItemsRoot.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), Node2D.Id, false));
            //Node2D.References.Add(new ReferenceNode(new NodeId(UAConst.Organizes), ItemsRoot.Id, true));
            //AddressSpaceTable.TryAdd(Node2D.Id, Node2D);
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
