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
using HumanOS.Kernel.PeSeL.DataLogger;
using HumanOS.Kernel.PeSeL.Script;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace HumanOS.PeSeL.NodeSpaceDataLogger.Script
{
  /// <summary>
  /// Implements the payload version 1 for the alarm events of the facility components.
  /// </summary>
  /// <remarks>
  /// Every alarm event (comes and goes) of an alarm event pool with the property
  /// EnableFacilityAlarms is sent to the FacilityComponentStream as the message-only field
  /// AlarmEvents. The message carries RefId = Guid.Empty like the structure message, the facility
  /// component id travels inside each entry (see DesignAlarmTracking.md, D3 and D4).
  /// </remarks>
  public class TFacilityAlarmPayload_v1 : TAbstractDataLoggerScriptObject<string>
  {
    ///<see cref="TAbstractDataLoggerScriptObject{T}"/>
    public override void initialize(IKernelAccess Kernel, TPayloadProcessingContext Context)
    {
      m_dicPools.Clear();
      m_ScanWatch.Reset();
    }

    ///<see cref="TAbstractDataLoggerScriptObject{T}"/>
    public override void postProcess(IKernelAccess Kernel, TPayloadProcessingContext Context)
    {
    }

    ///<see cref="TAbstractDataLoggerScriptObject{T}"/>
    public override string[] processPayload(IKernelAccess Kernel, TPayloadProcessingContext Context, List<TDataSet> lstData)
    {
      string[] astrRetval = new string[0];
      JArray jEvents = new JArray();

      //The datasets keep the order of the events, comes and goes of a buffered replay included
      foreach (TDataSet DataSet in lstData)
      {
        if (DataSet.Type == EDataSetType.AlarmEvent)
        {
          Guid FacilityComponentId = getFacilityComponentId(Kernel, DataSet.SourceNode);
          if (FacilityComponentId != Guid.Empty)
          {
            jEvents.Add(createEvent(FacilityComponentId, DataSet));
          }
          else
          {
            Logger.writeWarning($"Alarm pool '{DataSet.SourceNode.Name}' ({DataSet.SourceNode.GlobalId}) is not part of a facility component. Alarm '{DataSet.getFieldValue<string>("ConditionName")}' is skipped.");
          }
        } //DataSet.Type == EDataSetType.AlarmEvent
      } //end foreach

      if (jEvents.Count > 0)
      {
        JObject jFields = new JObject();
        jFields.Add("AlarmEvents", jEvents);

        JObject jMessage = new JObject();
        jMessage.Add("Stream", "FacilityComponentStream");
        jMessage.Add("RefId", Guid.Empty);
        jMessage.Add("TimeStamp", toIso(DateTime.UtcNow));
        jMessage.Add("State", 1);
        jMessage.Add("Fields", jFields);

        JObject jRoot = new JObject();
        jRoot.Add("Messages", new JArray(jMessage));

        Logger.writeVerbose(jRoot.ToString());
        astrRetval = new string[] { jRoot.ToString() };
      } //jEvents.Count > 0

      return astrRetval;
    }

    ///Creates the message entry of one alarm event
    private JObject createEvent(Guid FacilityComponentId, TDataSet DataSet)
    {
      bool bActive = DataSet.getFieldValue<bool>("Active");
      DateTime Disappearance = DataSet.getFieldValue<DateTime>("Disappearance");

      JObject jEvent = new JObject();
      jEvent["FacilityComponentId"] = FacilityComponentId;
      jEvent["ConditionName"] = DataSet.getFieldValue<string>("ConditionName");
      jEvent["Message"] = DataSet.getFieldValue<string>("Message");
      jEvent["SourceName"] = DataSet.getFieldValue<string>("SourceName");
      jEvent["AlarmType"] = DataSet.getFieldValue<int>("AlarmType");
      jEvent["Severity"] = DataSet.getFieldValue<int>("Severity");
      jEvent["Active"] = bActive;
      jEvent["Occurrence"] = toIso(DataSet.getFieldValue<DateTime>("Occurrence"));
      //An active alarm has no disappearance (the runtime keeps DateTime.MinValue)
      jEvent["Disappearance"] = !bActive && Disappearance > DateTime.MinValue ? new JValue(toIso(Disappearance)) : JValue.CreateNull();
      return jEvent;
    }

    ///Gets the facility component id of an alarm event pool, Guid.Empty if it is not part of a facility component
    private Guid getFacilityComponentId(IKernelAccess Kernel, INode Pool)
    {
      Guid Retval;

      if (!m_dicPools.TryGetValue(Pool.GlobalId, out Retval))
      {
        //A pool may appear after the last scan, but an unknown pool must not trigger a scan per event
        if (!m_ScanWatch.IsRunning || m_ScanWatch.ElapsedMilliseconds > 30000)
        {
          Logger.writeInfo("Scanning the facility components for alarm event pools...");
          m_dicPools.Clear();
          foreach (IGroupRelation DeviceGroup in Kernel.NodeSpace.queryNodesLocally(n => n.hasProperty("WorkplaceId") && n.hasProperty("DeviceId") && n is IGroupRelation))
          {
            addAlarmPools(DeviceGroup);
          }
          m_ScanWatch.Restart();
        }

        if (!m_dicPools.TryGetValue(Pool.GlobalId, out Retval))
        {
          Retval = Guid.Empty;
        }
      } //!m_dicPools.TryGetValue(Pool.GlobalId, out Retval)

      return Retval;
    }

    ///Maps the alarm event pools of a group and its sub groups to their facility component.
    ///The facility component is resolved like in TFacilityComponentPayload_v2.addFacilityComponents:
    ///the nearest parent group with a FacilityComponentType, the WorkplaceId overrides the id of a Workplace.
    private void addAlarmPools(IGroupRelation Group)
    {
      if (Group.hasProperty("FacilityComponentType"))
      {
        Guid FacilityComponentId = Group.GlobalId;
        if (Group.getProperty<string>("FacilityComponentType") == "Workplace")
        {
          Guid WorkplaceId = Group.getProperty<Guid>("WorkplaceId", Guid.Empty);
          FacilityComponentId = WorkplaceId != Guid.Empty ? WorkplaceId : Group.GlobalId;
        }

        //A sub group with its own facility component overwrites the pools of its parent, so the nearest group wins
        foreach (INode Pool in Group.queryNodesLocally(n => n is IAlarmEventPoolNode && n.hasProperty("EnableFacilityAlarms")))
        {
          m_dicPools[Pool.GlobalId] = FacilityComponentId;
        }
      }
      foreach (IGroupRelation SubGroup in Group.queryNodesLocally(n => n.hasProperty("DeviceId") && n is IGroupRelation))
      {
        addAlarmPools(SubGroup);
      }
    }

    ///Formats a time as UTC in the round trip format
    private string toIso(DateTime Time)
    {
      DateTime UtcTime = Time.Kind == DateTimeKind.Local ? Time.ToUniversalTime() : DateTime.SpecifyKind(Time, DateTimeKind.Utc);
      return UtcTime.ToString("o");
    }

    //Mapping of alarm event pool node ids to the id of their facility component
    private ConcurrentDictionary<Guid, Guid> m_dicPools = new ConcurrentDictionary<Guid, Guid>();

    //Stopwatch to limit the scans for unknown pools
    private readonly Stopwatch m_ScanWatch = new Stopwatch();
  }
}
