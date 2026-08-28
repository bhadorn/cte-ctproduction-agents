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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace HumanOS.PeSeL.NodeSpaceDataLogger.Script
{
  /// <summary>
  /// Implements the payload version 2 for platform streams
  /// </summary>
  public class TFacilityComponentPayload_v2 : TAbstractDataLoggerScriptObject<string>
  {
    ///<see cref="TAbstractDataLoggerScriptObject{T}"/>
    public override void initialize(IKernelAccess Kernel, TPayloadProcessingContext Context)
    {
      m_bFirstCall = true;
      Watch.Start();
    }

    ///<see cref="TAbstractDataLoggerScriptObject{T}"/>
    public override void postProcess(IKernelAccess Kernel, TPayloadProcessingContext Context)
    {
      m_bFirstCall = false;
    }

    ///<see cref="TAbstractDataLoggerScriptObject{T}"/>
    public override string[] processPayload(IKernelAccess Kernel, TPayloadProcessingContext Context, List<TDataSet> lstData)
    {
      JArray jMessages = new JArray();
      Dictionary<Guid, JObject> dicMessages = new Dictionary<Guid, JObject>();
      
      //Collects the facility component structure
      if (Watch.ElapsedMilliseconds > 120000 || m_bFirstCall)
      {
        Logger.writeInfo("Scanning the facility components for registration/updates...");
        JArray jComponents = new JArray();
        foreach(IGroupRelation DeviceGroup in Kernel.NodeSpace.queryNodesLocally(n => n.hasProperty("WorkplaceId") && n.hasProperty("DeviceId") && n is IGroupRelation))
        {
          addFacilityComponents(DeviceGroup, jComponents);
        }

        JObject jComponentMessage = new JObject();
        jComponentMessage.Add("Stream", "FacilityComponentStream");
        jComponentMessage.Add("RefId", Guid.Empty);
        jComponentMessage.Add("TimeStamp", DateTime.UtcNow.ToString("o"));
        jComponentMessage.Add("State", 1);
        JObject jFields = new JObject();
        jFields.Add("Components", jComponents);
        jComponentMessage.Add("Fields", jFields);
        jMessages.Add(jComponentMessage);
        Logger.writeInfo("...scanning the facility components done.");
        Watch.Reset();
        Watch.Restart();
      } //Watch.ElapsedMilliseconds > 120000 || m_bFirstCall

      //Processes the stream data
      foreach (TDataSet DataSet in lstData) 
      {
        //Gets the Id of the stream data node
        Guid Id = DataSet.getFieldValue<Guid>("Id");
        Guid FacilityComponentId = Guid.Empty;
        
        if (m_dicStreams.TryGetValue(Id, out FacilityComponentId))
        {
          string strStreamModelName = DataSet.Name;
          if (!dicMessages.ContainsKey(FacilityComponentId)) 
          {
            if (Kernel.NodeSpace.tryGetNodeLocally(FacilityComponentId, out INode DeviceNode) && DeviceNode.hasProperty("FacilityComponentType"))
            {
              DateTime TimeStamp = DataSet.getFieldValue<DateTime>("TimeStamp");
              if (Context.LastTimeStamp > TimeStamp)
              {
                TimeStamp = Context.LastTimeStamp;
              }
              dicMessages[FacilityComponentId] = new JObject();
              dicMessages[FacilityComponentId].Add("Stream", strStreamModelName);
              dicMessages[FacilityComponentId].Add("RefId", FacilityComponentId);
              dicMessages[FacilityComponentId].Add("TimeStamp", TimeStamp.ToString("o"));
              dicMessages[FacilityComponentId].Add("State", DataSet.getFieldValue<int>("State"));
              dicMessages[FacilityComponentId].Add("Fields", new JObject());
            }
          }
          if (dicMessages.ContainsKey(FacilityComponentId))
          {
            JObject jDevice = dicMessages[FacilityComponentId];
            if (DataSet.Type == EDataSetType.DataNode)
            {
              // Add platform data
              TGenericEntity nEntity = DataSet.getFieldValue<TGenericEntity>("Value");
              if (nEntity != null)
              {
                JObject jData = (JObject)jDevice.GetValue("Fields");
                foreach(KeyValuePair<string, object> FieldValue in DataSet.getFieldValue<TGenericEntity>("Value").getFieldValues())
                {
                  try
                  {
                    jData.Add(FieldValue.Key, FieldValue.Value != null ? JToken.FromObject(FieldValue.Value): null);
                  }
                  catch (Exception Exc)
                  {
                    Logger.writeWarning($"Failed to add '{FieldValue.Key}'. {Exc.Message}");
                  }
                }
              } //nEntity != null
            } //DataSet.Type == EDataSetType.DataNode
          } //dicMessages.ContainsKey(FacilityComponentId)
        } // m_dicStreams.TryGetValue(Id, out FacilityComponentId)
        else
        {
          Logger.writeWarning($"Streaming node '{Id}' not registered to a facility component id. Check the device data structure. Streams must be a subnode of the facility component.");
        }
      } //end foreach
      
      foreach(KeyValuePair<Guid, JObject> Message in dicMessages)
      {
        jMessages.Add(Message.Value);
      }

      JObject jRoot = new JObject();
      jRoot.Add("Messages", jMessages);
      
      Logger.writeVerbose(jRoot.ToString());
      return new string[]{jRoot.ToString()};
    }
    
    ///Adds facility components
    private void addFacilityComponents(IGroupRelation Group, JArray jCollection)
    {
      if (Group.hasProperty("FacilityComponentType"))
      {
        JObject jObject = new JObject();
        jObject["Id"] = Group.GlobalId;
        jObject["Name"] = Group.getProperty<string>("FacilityComponent");
        jObject["Type"] = Group.getProperty<string>("FacilityComponentType");
        jObject["SerialNumber"] = Group.getProperty<string>("MachineSerialNumber", "");
        jObject["InventoryNumber"] = Group.getProperty<string>("MachineInventoryNumber", "");
        jObject["SupplierName"] = Group.getProperty<string>("SupplierName", "");
        
        if (Group.getProperty<string>("FacilityComponentType") == "Workplace")
        {
          Guid WorkplaceId = Group.getProperty<Guid>("WorkplaceId", Guid.Empty);
          if (WorkplaceId == Guid.Empty)
          {
            WorkplaceId = Group.GlobalId;
          }
          jObject["WorkplaceId"] = WorkplaceId;
        }
        
        jCollection.Add(jObject);
        jCollection = new JArray();
        jObject["Components"] = jCollection;
        
        //Register all streams of the facility component
        foreach(INode StreamNode in Group.queryNodesLocally(n => n.hasProperty("EnableFacilityComponentStream") && n is IDataNode))
        {
          m_dicStreams[StreamNode.GlobalId] = Group.GlobalId;
        }
      }
      foreach(IGroupRelation SubGroup in Group.queryNodesLocally(n => n.hasProperty("DeviceId") && n is IGroupRelation))
      {
        addFacilityComponents(SubGroup, jCollection);
      }
    }
    
    ///Stopwatch to reduce registration messages
    private Stopwatch Watch {get;} = new Stopwatch();
    
    ///Flag is this is the first call
    private bool m_bFirstCall = false;
    
    //Mapping of streams (datanodes) to facility components
    private ConcurrentDictionary<Guid, Guid> m_dicStreams = new ConcurrentDictionary<Guid, Guid>();
  }
}
