/*****************************************************************************
 * Copyright (C) by CyberTech Engineering 2022 – www.cybertech.swiss         *
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
using HumanOS.Kernel.DataModel;
using HumanOS.Kernel.DataModel.Entity;
using HumanOS.Kernel.PeSeL.DataLogger;
using HumanOS.Kernel.PeSeL.Script;
using HumanOS.Kernel.Utils;
using HumanOS.Kernel.PeSeL.DataLogger.Config;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HumanOS.PeSeL.NodeSpaceDataLogger.Script
{
  /// <summary>
  /// Implements the payload version 2 for platform streams
  /// </summary>
  public class TStreamingPayload_v2 : TAbstractDataLoggerScriptObject<string>
  {
    ///<see cref="TAbstractDataLoggerScriptObject{T}"/>
    public override void initialize(IKernelAccess Kernel, TPayloadProcessingContext Context)
    {
    }

    ///<see cref="TAbstractDataLoggerScriptObject{T}"/>
    public override void postProcess(IKernelAccess Kernel, TPayloadProcessingContext Context)
    {
    }

    ///<see cref="TAbstractDataLoggerScriptObject{T}"/>
    public override string[] processPayload(IKernelAccess Kernel, TPayloadProcessingContext Context, List<TDataSet> lstData)
    {
      Dictionary<Guid, JObject> dicMessages = new Dictionary<Guid, JObject>();

      foreach (TDataSet DataSet in lstData) 
      {
        //Workplace Id can be used to override the DeviceId
        Guid DeviceId = DataSet.getFieldValue<Guid>("DeviceId");
        string strStreamModelName = DataSet.Name;

        if (!dicMessages.ContainsKey(DeviceId)) 
        {
          if (Kernel.NodeSpace.tryGetNodeLocally(DeviceId, out INode DeviceNode))
          {
            Guid WorkplaceId = DeviceNode.getProperty<Guid>("WorkplaceId", Guid.Empty);
            DateTime TimeStamp = DataSet.getFieldValue<DateTime>("TimeStamp");
            if (Context.LastTimeStamp > TimeStamp)
            {
              TimeStamp = Context.LastTimeStamp;
            }
            dicMessages[DeviceId] = new JObject();
            dicMessages[DeviceId].Add("Stream", strStreamModelName);
            dicMessages[DeviceId].Add("RefId", WorkplaceId == Guid.Empty ? DeviceId : WorkplaceId);
            dicMessages[DeviceId].Add("TimeStamp", TimeStamp.ToString("o"));
            dicMessages[DeviceId].Add("State", DataSet.getFieldValue<int>("State"));
            
            JObject Fields = new JObject();
            Fields.Add("DeviceName", DeviceNode.Name);
            dicMessages[DeviceId].Add("Fields", Fields);
          }
          else
          {
            Logger.writeError($"Could not find the device '{DeviceId}' in '{nameof(TStreamingPayload_v2)}'.");
          }
        }
        if (dicMessages.ContainsKey(DeviceId))
        {
          JObject jDevice = dicMessages[DeviceId];

          if (DataSet.Type == EDataSetType.DataNode)
          {
            // Add platform data
            TGenericEntity nEntity = DataSet.getFieldValue<TGenericEntity>("Value");
            if (nEntity != null)
            {
              JObject jData = (JObject)jDevice.GetValue("Fields");
              foreach(KeyValuePair<string, object> FieldValue in DataSet.getFieldValue<TGenericEntity>("Value").getFieldValues())
              {
                jData.Add(FieldValue.Key, FieldValue.Value != null ? JToken.FromObject(FieldValue.Value): null);
              }
            }
          }
        }
      }
      JArray jMessages = new JArray();
      foreach(KeyValuePair<Guid, JObject> Message in dicMessages)
      {
        jMessages.Add(Message.Value);
      }

      JObject jRoot = new JObject();
      jRoot.Add("Messages", jMessages);
      
      return new string[]{jRoot.ToString()};
    }
  }
}
