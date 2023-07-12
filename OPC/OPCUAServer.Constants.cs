/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Xml;
using System.Runtime.Serialization;
using LibUA.Core;

namespace OPCUAServer
{
    #region Object Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Objects
    {
        /// <remarks />
        public const uint ADUSent1 = 49;

        /// <remarks />
        public const uint ADUSent1_ADUSent = 73;

        /// <remarks />
        public const uint ADUReceived1 = 97;

        /// <remarks />
        public const uint ADUReceived1_ADUReceived = 121;
    }
    #endregion

    #region ObjectType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypes
    {
        /// <remarks />
        public const uint ADUSent = 1;

        /// <remarks />
        public const uint ADUReceived = 25;
    }
    #endregion

    #region Variable Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Variables
    {
        /// <remarks />
        public const uint ADUSent_ProcessOrder = 2;

        /// <remarks />
        public const uint ADUSent_DateTime = 3;

        /// <remarks />
        public const uint ADUSent_MaterialCode = 4;

        /// <remarks />
        public const uint ADUSent_Quantity = 5;

        /// <remarks />
        public const uint ADUSent_Quantity_EURange = 9;

        /// <remarks />
        public const uint ADUSent_QuantityUOM = 11;

        /// <remarks />
        public const uint ADUSent_Lot = 12;

        /// <remarks />
        public const uint ADUSent_PlantCode = 13;

        /// <remarks />
        public const uint ADUSent_DeviceIdentifier = 14;

        /// <remarks />
        public const uint ADUSent_VesselID = 15;

        /// <remarks />
        public const uint ADUSent_ItemMaterial = 16;

        /// <remarks />
        public const uint ADUSent_BatchStepID = 17;

        /// <remarks />
        public const uint ADUSent_NewDosingRequest = 18;

        /// <remarks />
        public const uint ADUSent_ProcessOrderDataSent = 19;

        /// <remarks />
        public const uint ADUSent_RequestAccepted = 20;

        /// <remarks />
        public const uint ADUSent_ReadyForNewDosing = 21;

        /// <remarks />
        public const uint ADUSent_ConsumptionAccepted = 22;

        /// <remarks />
        public const uint ADUSent_DosingCompleted = 23;

        /// <remarks />
        public const uint ADUSent_Watchdog = 24;

        /// <remarks />
        public const uint ADUReceived_ProcessOrder = 26;

        /// <remarks />
        public const uint ADUReceived_DateTime = 27;

        /// <remarks />
        public const uint ADUReceived_MaterialCode = 28;

        /// <remarks />
        public const uint ADUReceived_ActualQuantity = 29;

        /// <remarks />
        public const uint ADUReceived_ActualQuantity_EURange = 33;

        /// <remarks />
        public const uint ADUReceived_QuantityUOM = 35;

        /// <remarks />
        public const uint ADUReceived_Lot = 36;

        /// <remarks />
        public const uint ADUReceived_PlantCode = 37;

        /// <remarks />
        public const uint ADUReceived_DeviceIdentifier = 38;

        /// <remarks />
        public const uint ADUReceived_VesselID = 39;

        /// <remarks />
        public const uint ADUReceived_ItemMaterial = 40;

        /// <remarks />
        public const uint ADUReceived_BatchStepID = 41;

        /// <remarks />
        public const uint ADUReceived_NewDosingRequest = 42;

        /// <remarks />
        public const uint ADUReceived_ProcessOrderDataSent = 43;

        /// <remarks />
        public const uint ADUReceived_RequestAccepted = 44;

        /// <remarks />
        public const uint ADUReceived_ReadyForNewDosing = 45;

        /// <remarks />
        public const uint ADUReceived_ConsumptionAccepted = 46;

        /// <remarks />
        public const uint ADUReceived_DosingCompleted = 47;

        /// <remarks />
        public const uint ADUReceived_Watchdog = 48;

        
    }
    #endregion
 

    #region BrowseName Declarations
    /// <remarks />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class BrowseNames
    {
        /// <remarks />
        public const string ActualQuantity = "ActualQuantity";

        /// <remarks />
        public const string ADUReceived = "ADUReceived";

        /// <remarks />
        //public const string ADUReceived1 = "ADUReceived #1";

        /// <remarks />
        public const string ADUSent = "ADUSent";

        /// <remarks />
        //public const string ADUSent1 = "ADUSent #1";

        /// <remarks />
        public const string BatchStepID = "BatchStepID";

        /// <remarks />
        public const string ConsumptionAccepted = "ConsumptionAccepted";

        /// <remarks />
        public const string DateTime = "DateTime";

        /// <remarks />
        public const string DeviceIdentifier = "DeviceIdentifier";

        /// <remarks />
        public const string DosingCompleted = "DosingCompleted";

        /// <remarks />
        public const string ItemMaterial = "ItemMaterial";

        /// <remarks />
        public const string Lot = "Lot";

        /// <remarks />
        public const string MaterialCode = "MaterialCode";

        /// <remarks />
        public const string NewDosingRequest = "NewDosingRequest";

        /// <remarks />
        public const string PlantCode = "PlantCode";

        /// <remarks />
        public const string ProcessOrder = "ProcessOrder";

        /// <remarks />
        public const string ProcessOrderDataSent = "ProcessOrderDataSent";

        /// <remarks />
        public const string Quantity = "Quantity";

        /// <remarks />
        public const string QuantityUOM = "QuantityUOM";

        /// <remarks />
        public const string ReadyForNewDosing = "ReadyForNewDosing";

        /// <remarks />
        public const string RequestAccepted = "RequestAccepted";

        /// <remarks />
        public const string VesselID = "VesselID";

        /// <remarks />
        public const string Watchdog = "Watchdog";
    }
    #endregion

    #region Namespace Declarations
    /// <remarks />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Namespaces
    {
        /// <summary>
        /// The URI for the OpcUa namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUa = "http://opcfoundation.org/UA/";

        /// <summary>
        /// The URI for the OpcUaXsd namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUaXsd = "http://opcfoundation.org/UA/2008/02/Types.xsd";

        /// <summary>
        /// The URI for the SanTintOPCUAServer namespace (.NET code namespace is 'OPCUAServer').
        /// </summary>
        public const string SanTintOPCUAServer = "http://opcfoundation.org/OPCUAServer";
    }
    #endregion
}