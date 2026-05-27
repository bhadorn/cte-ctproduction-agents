/*****************************************************************************
 * Copyright (C) by CyberTech Engineering 2026 – www.cybertech.swiss         *
 *****************************************************************************
 * Project: HumanOS (R)
 * Date   : 2026
 *****************************************************************************
 * License:                                                                  *
 *   This library is protected software; you are not allowed to redistribute *
 *   whole or part of it to other companies or external persons without the  *
 *   authorization of the CEO CyberTech Engineering GmbH.                    *
 *****************************************************************************/

using HumanOS.Kernel;
using HumanOS.Kernel.Communication;
using HumanOS.Kernel.Communication.Http;
using HumanOS.Kernel.DataModel;
using HumanOS.Kernel.UHAL.Device;
using HumanOS.Kernel.UHAL.InfoModel;
using HumanOS.Kernel.UHAL.Script;
using HumanOS.Kernel.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace HumanOS.IoT.Designer.Library.Scripts
{
  /// <summary>
  /// Implements the http stream task processor to extract data from MT-Connect
  /// </summary>
  public class THaasControlMtConnectStream : TAbstractHttpStreamScriptObject
  {
    ///<see cref="TAbstractHttpStreamScriptObject"/>
    public override void handleStream(IKernelAccess Kernel,
                                      TDeviceSchemaInfo DeviceInfo,
                                      IHttpStream DataStream)
    {
      try
      {
        THttpResponse Response = DataStream.request("current", "GET", "", "text/xml", new Dictionary<string, string>());

        //Read the data nodes from the device
        IDataNode<string> OperationMode = Kernel.NodeSpace.getNode<IDataNode<string>>(DeviceInfo.DataNodes.First(n => n.Name == "OperationMode").Id);
        IDataNode<string> RunningState = Kernel.NodeSpace.getNode<IDataNode<string>>(DeviceInfo.DataNodes.First(n => n.Name == "RunningState").Id);
        IDataNode<string> MainProgramName = Kernel.NodeSpace.getNode<IDataNode<string>>(DeviceInfo.DataNodes.First(n => n.Name == "MainProgramName").Id);
        IDataNode<int> Available = Kernel.NodeSpace.getNode<IDataNode<int>>(DeviceInfo.DataNodes.First(n => n.Name == "Available").Id);
        IDataNode<int> PartCounter = Kernel.NodeSpace.getNode<IDataNode<int>>(DeviceInfo.DataNodes.First(n => n.Name == "PartCounter").Id);
        IDataNode<int> AlarmState = Kernel.NodeSpace.getNode<IDataNode<int>>(DeviceInfo.DataNodes.First(n => n.Name == "AlarmState").Id);
        IDataNode<double> FeedrateOverride = Kernel.NodeSpace.getNode<IDataNode<double>>(DeviceInfo.DataNodes.First(n => n.Name == "FeedrateOverride").Id);
        
        //Parse the xml content and set the MT-Connect namespace
        XDocument Doc = XDocument.Parse(Response.Content);
        XmlNamespaceManager Manager = new XmlNamespaceManager(Doc.CreateReader().NameTable);
        Manager.AddNamespace("m", "urn:mtconnect.org:MTConnectStreams:1.2");

        //Extract the data
        string strAvailableValue = Doc.XPathSelectElement("//m:*[@dataItemId='avail']", Manager)?.Value;
        string strOperationModeValue = Doc.XPathSelectElement("//m:*[@dataItemId='mode']", Manager)?.Value;
        string strRunningState = Doc.XPathSelectElement("//m:*[@dataItemId='rstat']", Manager)?.Value;
        string strPartCounter = Doc.XPathSelectElement("//m:*[@dataItemId='m30c1']", Manager)?.Value;
        string strFeedrateOverride = Doc.XPathSelectElement("//m:*[@dataItemId='fdovrd']", Manager)?.Value;
        string strEmergencyStop = Doc.XPathSelectElement("//m:*[@dataItemId='estop']", Manager)?.Value;
        string strMainProgramName = Doc.XPathSelectElement("//m:*[@dataItemId='ncprog']", Manager)?.Value;
        int iAlarmCount = Doc.XPathSelectElements("//m:Alarm", Manager).Count();

        //Set the data to data nodes
        Available.passValue(strAvailableValue == "AVAILABLE" ? 1 : 0, false);
        OperationMode.passValue(strOperationModeValue, false);
        RunningState.passValue(strRunningState, false);
        MainProgramName.passValue(strMainProgramName, false);
        PartCounter.passValue(strPartCounter != null ? strPartCounter.toInt(0) : 0, false);
        AlarmState.passValue(iAlarmCount > 0 || strEmergencyStop != "ARMED" ? 1 : 0, false);
        FeedrateOverride.passValue(strFeedrateOverride != null ? strFeedrateOverride.toInt(0) : 0, false);
      }
      catch (Exception Exc)
      {
        Logger.writeError($"Failed to read data. {Exc.Message}");
      }
    }
  }
}
