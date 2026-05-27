/*****************************************************************************
 * Copyright (C) by CyberTech Engineering 2022 – www.cybertech.swiss         *
 *****************************************************************************
 * Project: HumanOS (R)
 * Date   : 2022
 *****************************************************************************
 * License:                                                                  *
 *   This library is protected software; you are not allowed to redistribute *
 *   whole or part of it to other companies or external persons without the  *
 *   authorization of the CEO CyberTech Engineering GmbH.                    *
 *****************************************************************************/

using HumanOS.Kernel;
using HumanOS.Kernel.DataModel;
using HumanOS.Kernel.Processing;
using HumanOS.Kernel.UHAL.Script;
using System;
using System.Collections.Generic;

namespace HumanOS.UHAL.FanucControl.Scripts
{
  /// <summary>
  /// Implements the the processor to extract product name and production step
  /// out of the program header 
  /// </summary>
  public class TFanucProductProcessor : TAbstractProcessingScriptObject
  {
    ///<see cref="TAbstractProcessingScriptObject"/>
    public override void process(IProcessingNode Processor)
    {
      string strHeader = Processor.getProperty<string>("InputProgramHeader");

      string strProduct = "";
      string strStep = "";
      if (!string.IsNullOrEmpty(strHeader))
      {
        string[] astrTokens = strHeader.Replace("(", " ")
                                       .Replace(")", "")
                                       .Split(' ');
        if (astrTokens.Length > 2)
        {
          strStep = astrTokens[1];
          strProduct = astrTokens[2];
        }
        else if (astrTokens.Length > 1)
        {
          strProduct = astrTokens[astrTokens.Length - 1];
        }

      }
      Processor.setProperty<string>("OutputProductName", strProduct);
      Processor.setProperty<string>("OutputProductionStep", strStep);
    }
  }
}
