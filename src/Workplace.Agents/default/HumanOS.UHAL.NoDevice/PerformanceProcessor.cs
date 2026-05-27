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
using System.Diagnostics;

namespace HumanOS.IoT.Designer.Library.Scripts
{
  /// <summary>
  /// Script to measure the job performance
  /// </summary>
  public class TPerformanceProcessor : TAbstractProcessingScriptObject
  {
  
    private const int JobState_Production = 400;
    private const int JobState_ProductionMax = 499;
    
    ///<see cref="TAbstractProcessingScriptObject"/>
    public override void process(IProcessingNode Processor)
    {
      if (m_bFirstRun)
      {
        m_bFirstRun = false;
        m_CycleTimeWatch.Stop();
        m_CycleTimeWatch.Reset();
        Processor.setProperty<double>("LastCycleTime", 0);
        Processor.setProperty<double>("MachinePerformance", 1);
      }
    
      int iMachineState = Processor.getProperty<int>("MachineState");
      int iJobState = Processor.getProperty<int>("JobState");
      int iPartCounter = Processor.getProperty<int>("PartCounter");
      double fSetCycleTime = Processor.getProperty<double>("SetCycleTime");

      
      if (iJobState >= JobState_Production && iJobState <= JobState_ProductionMax)
      {
        //New part -> reset the counter
        if (iPartCounter != m_iLastPartCounter)
        {
          double fCurrentQuantity = Processor.getProperty<double>("CurrentQuantity");
          m_CycleTimeWatch.Stop();
          
          double fCycleTime = m_CycleTimeWatch.ElapsedMilliseconds / 60000.0;
          double fMachinePerformance = 1;
          Processor.setProperty<double>("LastCycleTime", fCycleTime);
          if (TFloat.isNonZero(fSetCycleTime) && TFloat.isNonZero(fCycleTime))
          {
            fMachinePerformance = fSetCycleTime / fCycleTime;
            if (fMachinePerformance > 1)
            {
              fMachinePerformance = 1;
            }
          }
          m_CycleTimeWatch.Reset();
          m_iLastPartCounter = iPartCounter;
          Processor.setProperty<double>("ProducedQuantity", fCurrentQuantity + 1);
          Processor.setProperty<double>("MachinePerformance", fMachinePerformance);
        }
      
        //Machine is in production and has a job assigned
        else if (iMachineState >= 200 && iMachineState <= 299)
        {
          if (!m_CycleTimeWatch.IsRunning)
          {
            m_CycleTimeWatch.Start();
          }
        }
        else
        {
          m_CycleTimeWatch.Stop();
        }
      } //iJobState == JobState_Production
      else
      {
        m_CycleTimeWatch.Stop();
        m_iLastPartCounter = iPartCounter;
      } //iJobState != JobState_Production

      Processor.setProperty<double>("CurrentCycleTime", m_CycleTimeWatch.ElapsedMilliseconds / 60000.0);
    }
    
    private Stopwatch m_CycleTimeWatch = new Stopwatch();
    private int m_iLastPartCounter = 0;
    private bool m_bFirstRun = true;
  }
}
