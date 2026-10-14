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
        //The scan repopulates both mappings completely, so stale entries of detached stream
        //nodes or of groups that lost their FacilityComponentType must not survive it.
        m_dicStreams.Clear();
        m_dicRefIds.Clear();
        JArray jComponents = new JArray();
        JArray jAlarmSnapshot = new JArray();
        foreach(IGroupRelation DeviceGroup in Kernel.NodeSpace.queryNodesLocally(n => n.hasProperty("WorkplaceId") && n.hasProperty("DeviceId") && n is IGroupRelation))
        {
          addFacilityComponents(DeviceGroup, jComponents, jAlarmSnapshot);
        }

        JObject jComponentMessage = new JObject();
        jComponentMessage.Add("Stream", "FacilityComponentStream");
        jComponentMessage.Add("RefId", Guid.Empty);
        jComponentMessage.Add("TimeStamp", DateTime.UtcNow.ToString("o"));
        jComponentMessage.Add("State", 1);
        JObject jFields = new JObject();
        jFields.Add("Components", jComponents);
        jFields.Add("SessionId", m_SessionId);
        //The snapshot of the alarm event pools reconciles the alarm events (DesignAlarmTracking.md, 4.2)
        if (jAlarmSnapshot.Count > 0)
        {
          jFields.Add("AlarmSnapshot", jAlarmSnapshot);
        }
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
        Guid ComponentNodeId = Guid.Empty;
        
        if (m_dicStreams.TryGetValue(Id, out ComponentNodeId))
        {
          //The reference id may be overridden by the WorkplaceId and is therefore not unique
          //across device groups. The messages are grouped by the node id of the facility
          //component instead, so two groups sharing a WorkplaceId keep their own message.
          Guid FacilityComponentId;
          if (!m_dicRefIds.TryGetValue(ComponentNodeId, out FacilityComponentId))
          {
            FacilityComponentId = ComponentNodeId;
          }
          string strStreamModelName = DataSet.Name;
          if (!dicMessages.ContainsKey(ComponentNodeId))
          {
            DateTime TimeStamp = DataSet.getFieldValue<DateTime>("TimeStamp");
            if (Context.LastTimeStamp > TimeStamp)
            {
              TimeStamp = Context.LastTimeStamp;
            }
            dicMessages[ComponentNodeId] = new JObject();
            dicMessages[ComponentNodeId].Add("Stream", strStreamModelName);
            dicMessages[ComponentNodeId].Add("RefId", FacilityComponentId);
            dicMessages[ComponentNodeId].Add("TimeStamp", TimeStamp.ToString("o"));
            dicMessages[ComponentNodeId].Add("State", DataSet.getFieldValue<int>("State"));
            dicMessages[ComponentNodeId].Add("Fields", new JObject());
          }
          if (dicMessages.ContainsKey(ComponentNodeId))
          {
            JObject jDevice = dicMessages[ComponentNodeId];
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
          } //dicMessages.ContainsKey(ComponentNodeId)
        } // m_dicStreams.TryGetValue(Id, out ComponentNodeId)
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
    private void addFacilityComponents(IGroupRelation Group, JArray jCollection, JArray jAlarmSnapshot)
    {
      if (Group.hasProperty("FacilityComponentType"))
      {
        Guid FacilityComponentId = Group.GlobalId;
        JObject jObject = new JObject();
        jObject["Name"] = Group.getProperty<string>("FacilityComponent");
        jObject["Type"] = Group.getProperty<string>("FacilityComponentType");
        jObject["DeviceId"] = Group.getProperty<Guid>("DeviceId");
        jObject["SerialNumber"] = Group.getProperty<string>("MachineSerialNumber", "");
        jObject["InventoryNumber"] = Group.getProperty<string>("MachineInventoryNumber", "");
        jObject["Manufacturer"] = Group.getProperty<string>("Manufacturer", "");
        jObject["YearOfConstruction"] = parseIntProperty(Group.getProperty<string>("MachineYearOfConstruction", ""));
        jObject["Criticality"] = parseIntProperty(Group.getProperty<string>("MachineCriticality", ""));
        
        //Special case Workplace: 
        // 1. a workplace is typically the root facility component in ctProduction
        // 2. if a WorkplaceId property is given (mapping to a dedicated workplace in the platform)
        //    then the facility component id equals that workplace id too
        if (Group.getProperty<string>("FacilityComponentType") == "Workplace")
        {
          Guid WorkplaceId = Group.getProperty<Guid>("WorkplaceId", Guid.Empty);
          if (WorkplaceId == Guid.Empty)
          {
            WorkplaceId = Group.GlobalId;
          }
          jObject["WorkplaceId"] = WorkplaceId;
          FacilityComponentId = WorkplaceId;
        }
        jObject["Id"] = FacilityComponentId;
        
        jCollection.Add(jObject);
        jCollection = new JArray();
        jObject["Components"] = jCollection;
        
        //Register all streams of the facility component. The streams are mapped to the node id
        //of the group, which stays unique even when the reference id is overridden.
        m_dicRefIds[Group.GlobalId] = FacilityComponentId;
        foreach(INode StreamNode in Group.queryNodesLocally(n => n.hasProperty("EnableFacilityComponentStream") && n is IDataNode))
        {
          m_dicStreams[StreamNode.GlobalId] = Group.GlobalId;
        }

        addAlarmSnapshot(Group, FacilityComponentId, jAlarmSnapshot);
      }
      foreach(IGroupRelation SubGroup in Group.queryNodesLocally(n => n.hasProperty("DeviceId") && n is IGroupRelation))
      {
        addFacilityComponents(SubGroup, jCollection, jAlarmSnapshot);
      }
    }

    ///Adds the snapshot of the alarm event pools of a facility component (DesignAlarmTracking.md, 4.2).
    ///An empty alarm list means that no alarm is active. A component without pool or with a controller which
    ///is not connected is left out: unknown is not the same as no alarm active.
    private void addAlarmSnapshot(IGroupRelation Group, Guid FacilityComponentId, JArray jSnapshot)
    {
      List<TAlarmItem> lstAlarms = new List<TAlarmItem>();
      bool bHasPool = false;
      foreach (INode Node in Group.queryNodesLocally(n => n is IAlarmEventPoolNode && n.hasProperty("EnableFacilityAlarms")))
      {
        IAlarmEventPoolNode Pool = Node as IAlarmEventPoolNode;
        if (Pool != null)
        {
          bHasPool = true;
          lstAlarms.AddRange(Pool.PendingAlarms);
        }
      }

      if (bHasPool && isControllerConnected(Group))
      {
        DateTime ReadTime = DateTime.UtcNow;
        lstAlarms.Sort((a, b) => string.CompareOrdinal(a.ConditionName, b.ConditionName));

        JArray jAlarms = new JArray();
        foreach (TAlarmItem Alarm in lstAlarms.Take(c_iMaxSnapshotAlarms))
        {
          JObject jAlarm = new JObject();
          jAlarm["ConditionName"] = Alarm.ConditionName;
          jAlarm["Message"] = Alarm.Message;
          jAlarm["SourceName"] = Alarm.SourceName;
          jAlarm["AlarmType"] = (int)Alarm.AlarmType;
          jAlarm["Severity"] = Alarm.Severity;
          jAlarm["Occurrence"] = toIso(Alarm.Occurrence);
          jAlarms.Add(jAlarm);
        }

        JObject jEntry = new JObject();
        jEntry["FacilityComponentId"] = FacilityComponentId;
        jEntry["TimeStamp"] = toIso(ReadTime);
        jEntry["Alarms"] = jAlarms;
        //The platform only opens alarms from a cut list and never closes any
        if (lstAlarms.Count > c_iMaxSnapshotAlarms)
        {
          jEntry["Truncated"] = true;
        }
        jSnapshot.Add(jEntry);
      } //bHasPool && isControllerConnected(Group)
    }

    ///Checks if the controller of a facility component is connected, based on its Available data node
    ///(PortMatchId AvailableType, a value greater than 0 means available). Without such a node the
    ///connection state is unknown.
    private bool isControllerConnected(IGroupRelation Group)
    {
      bool bRetval = false;
      IDataNode Available = Group.queryNodesLocally(n => n.Name == "Available" && n is IDataNode).FirstOrDefault() as IDataNode;
      if (Available != null)
      {
        try
        {
          bRetval = Available.DataState == EDataState.Good && Convert.ToInt32(Available.ObjectValue) > 0;
        }
        catch (Exception Exc)
        {
          Logger.writeWarning($"Could not read the Available node of '{Group.getProperty<string>("FacilityComponent", "")}'. {Exc.Message}");
        }
      }
      return bRetval;
    }

    ///Formats a time as UTC in the round trip format
    private string toIso(DateTime Time)
    {
      DateTime UtcTime = Time.Kind == DateTimeKind.Local ? Time.ToUniversalTime() : DateTime.SpecifyKind(Time, DateTimeKind.Utc);
      return UtcTime.ToString("o");
    }

    ///Parses a machine property configured as free text in the Designer into an int, defaulting to 0 when absent or not a valid number
    private int parseIntProperty(string strValue)
    {
      int iRetval = 0;
      if (!string.IsNullOrWhiteSpace(strValue))
      {
        int.TryParse(strValue, out iRetval);
      }
      return iRetval;
    }

    ///Maximum number of alarms of one snapshot entry
    private const int c_iMaxSnapshotAlarms = 200;

    ///Stopwatch to reduce registration messages
    private Stopwatch Watch {get;} = new Stopwatch();
    
    ///Flag is this is the first call
    private bool m_bFirstCall = false;
    
    //Mapping of streams (datanodes) to the node id of their facility component group
    private ConcurrentDictionary<Guid, Guid> m_dicStreams = new ConcurrentDictionary<Guid, Guid>();
    
    //Mapping of facility component group node ids to their reference id, which is the
    //WorkplaceId for a workplace component and the node id itself for all others
    private ConcurrentDictionary<Guid, Guid> m_dicRefIds = new ConcurrentDictionary<Guid, Guid>();

    //Id of this agent session: a new process (or script instance) gets a new id, so the platform can
    // tell an agent restart from a platform restart or a periodic scan
    private readonly Guid m_SessionId = Guid.NewGuid();
  }
}
