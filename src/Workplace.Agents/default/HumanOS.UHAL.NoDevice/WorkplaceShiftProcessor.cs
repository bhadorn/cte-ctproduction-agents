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

using HumanOS.Kernel.Processing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HumanOS.IoT.Designer.Library.Scripts
{
  /// <summary>
  /// Processing script to evaluate if the workshift is active
  /// </summary>
  public class TWorkplaceShiftProcessorScript : TAbstractProcessingScriptObject
  {
    ///<see cref="TAbstractProcessingScriptObject"/>
    public override void process(IProcessingNode Processor)
    {
      string nstrValue = Processor.getProperty<string>("WorkShiftSetting");
      bool bIsShiftActive = true;
      if (nstrValue != null)
      {
        try
        {
          if (nstrValue != m_nstrPreviousData || m_nCurrentShift == null)
          {
            m_nCurrentShift = JsonConvert.DeserializeObject<TShiftData>(nstrValue);
            m_nstrPreviousData = nstrValue;
          }
          
          if (m_nCurrentShift != null)
          {
            switch(DateTime.Now.DayOfWeek)
            {
              case DayOfWeek.Monday:
                bIsShiftActive = evalShift(m_nCurrentShift.Monday);
                break;
              case DayOfWeek.Tuesday:
                bIsShiftActive = evalShift(m_nCurrentShift.Tuesday);
                break;
              case DayOfWeek.Wednesday:
                bIsShiftActive = evalShift(m_nCurrentShift.Wednesday);
                break;
              case DayOfWeek.Thursday:
                bIsShiftActive = evalShift(m_nCurrentShift.Thursday);
                break;
              case DayOfWeek.Friday:
                bIsShiftActive = evalShift(m_nCurrentShift.Friday);
                break;
              case DayOfWeek.Saturday:
                bIsShiftActive = evalShift(m_nCurrentShift.Saturday);
                break;
              case DayOfWeek.Sunday:
                bIsShiftActive = evalShift(m_nCurrentShift.Sunday);
                break;
            } //end switch
          } //m_nCurrentShift != null
        }
        catch(Exception Exc) when (Exc.isNotCancelException())
        {
          Logger.writeError($"Failed to process the shift data. {Exc.Message}");
        }
      } //nstrValue != null
      
      Processor.setProperty<bool>("AfterWorkShift", !bIsShiftActive);
    }
    
    //Evaluates the shift
    private bool evalShift(List<TTimeRange> lstShiftTimes)
    {
      TimeSpan CurrentTime = DateTime.Now.TimeOfDay;
      return  lstShiftTimes.Any(n => CurrentTime >= n.Start && CurrentTime < n.End);
    }
    
    //Simple time range structure
    private class TTimeRange
    {
      public TimeSpan Start {get; set;}
      public TimeSpan End {get; set;}
    }
    
    //Shift data
    private class TShiftData
    {
      public List<TTimeRange> Monday {get; set; } 
      public List<TTimeRange> Tuesday {get; set; } 
      public List<TTimeRange> Wednesday {get; set; } 
      public List<TTimeRange> Thursday {get; set; } 
      public List<TTimeRange> Friday {get; set; } 
      public List<TTimeRange> Saturday {get; set; } 
      public List<TTimeRange> Sunday {get; set; } 
    }
    
    //previous data read from port
    private string m_nstrPreviousData;
    
    //current factored shift object
    private TShiftData m_nCurrentShift;
  }
}
