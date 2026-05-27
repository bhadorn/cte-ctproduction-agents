/*****************************************************************************
 * Copyright (C) by CyberTech Engineering 2024 – www.cybertech.swiss         *
 *****************************************************************************
 * Project: HumanOS (R)
 * Date   : 2024
 *****************************************************************************
 * License:                                                                  *
 *   This library is protected software; you are not allowed to redistribute *
 *   whole or part of it to other companies or external persons without the  *
 *   authorization of the CEO CyberTech Engineering GmbH.                    *
 *****************************************************************************/

using CyberTech;
using HumanOS.Kernel.Processing;
using HumanOS.Kernel.DataModel.Entity;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HumanOS.IoT.Designer.Library.Scripts
{
  /// <summary>
  /// This script handles the micro stops of a machine.
  /// When when, the micro stops are regarded as production time
  /// </summary>
  public class TWorkplaceStateStreamFilter : TAbstractProcessingScriptObject
  {
    ///<see cref="TAbstractProcessingScriptObject"/>
    public override void process(IProcessingNode Processor)
    {
      double fMaxChangeOverTime = Processor.getProperty<double>("MaxChangeOverTime");
      if (PendingStopState != null && StopWatch.Elapsed.TotalMinutes > fMaxChangeOverTime)
      {
        Logger.writeDebug($"Stop exceeds the expected changeover time of {fMaxChangeOverTime} ms. Send stop.");
        Processor.setProperty<TGenericEntity>("OutputPort", PendingStopState);
        PendingStopState = null;
      }
    
      TGenericEntity Entity = Processor.getProperty<TGenericEntity>("WorkplaceStateStreamFilter_InputPort");
      if (Entity.Id != LastEntityId)
      {
        LastEntityId = Entity.Id;
      
        bool bSendState = true;
        int iCurrentMachineState = Entity.getValue<int>("MachineState");
      
        // If max changeover time is set -> check the stops
        // else send the state immediately
        if (TFloat.isPositive(fMaxChangeOverTime))
        {
          //Current state is production
          if (iCurrentMachineState >= 200 && iCurrentMachineState <= 299)
          {
            // The pending stop is processed but its machine state is changed to "Production"
            // Reason is, that the WorkplaceState entity also contains part counter, last cycletime etc.
            // These data must be passed to the platform
            if (PendingStopState != null)
            {
              Logger.writeDebug($"Detected changeover. Stop ignored.");
              PendingStopState.setValue("MachineState", iCurrentMachineState);
              Processor.setProperty<TGenericEntity>("OutputPort", PendingStopState);
              PendingStopState = null;
            }
            LastProductionState = Entity;
          }
          else if (iCurrentMachineState >= 300 && iCurrentMachineState <= 399 && LastProductionState != null && PendingStopState == null)
          {
            PendingStopState = Entity;
            StopWatch.Reset();
            StopWatch.Restart();
            bSendState = false; //wait until production has changed
            Logger.writeDebug($"Detected stop after production. Keep state in memory.");
          }
      
          //All other states reset the microstops
          else
          {
            //Pending stop state -> send it now
            if (PendingStopState != null)
            {
              Logger.writeDebug($"Ignore changeover due to other state '{iCurrentMachineState}'. Send stop.");
              Processor.setProperty<TGenericEntity>("OutputPort", PendingStopState);
              PendingStopState = null;
            }
            LastProductionState = null;
          }
        } //TFloat.isPositive(fMaxChangeOverTime)
      
        if (bSendState)
        {
          Logger.writeDebug($"Sending status '{iCurrentMachineState}'.");
          Processor.setProperty<TGenericEntity>("OutputPort", Entity);
        }
      } //Entity.Id != LastEntityId
    }
    
    /// Id of the last entity sent
    private Guid LastEntityId { get; set; }
    
    /// Entity of the last production state
    private TGenericEntity LastProductionState { get; set; }
    
    /// Entity of the pending stop state
    private TGenericEntity PendingStopState { get; set; }
    
    /// Stopwatch to check the changeover time
    private Stopwatch StopWatch { get; } = new Stopwatch();
  }
}
