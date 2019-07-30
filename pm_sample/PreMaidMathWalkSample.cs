using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PreMaid.MathWalkSampleCode
{
    /// <summary>
    /// 計算で歩行モーションを作成するプログラム（動作品より抜粋）
    /// </summary>
    public class PreMaidXXXXXXXX : MonoBehaviour
    {
                 ：
                前略
                 ：

        private int TickSpeed = 9;                      // PreMaidでは 内部15ms／SPEED指定も15ms刻みとなる。
        private float _poseProcessDelay = 0.015f * 9;   // 15ms * 9 = 135ms ( 7.4FPS )

        private int maxTick    = 10;   // １周期（左右）を 2PI として、それを１０分割する。（転送レート依存）
        private int curTick    =  0;   // １周期（左右）１０分割の現在位置。

        // sin, sin^3, plot1, plot2 を配列で持つ（XLS表を参照のこと）
        private float[] table_sin1  = {  0.00f,  0.59f,  0.95f,  0.95f,  0.59f,  0.00f, -0.59f, -0.95f, -0.95f, -0.59f };
        private float[] table_sin3  = {  0.00f,  0.20f,  0.86f,  0.86f,  0.20f,  0.00f, -0.20f, -0.86f, -0.86f, -0.20f };
        private float[] table_plot1 = {  0.70f,  0.90f,  0.60f, -0.40f, -0.90f, -0.80f, -0.50f, -0.20f,  0.10f,  0.40f };
        private float[] table_plot2 = { -0.80f, -0.50f, -0.20f,  0.10f,  0.40f,  0.70f,  0.90f,  0.60f, -0.40f, -0.90f };

        private int WalkingCondition = 0;   // 歩行状態 停止はゼロ：予備動作１：操作可能２ : 停止前３
        private int WalkingSteering = 0;

        // 歩行に使うサーボの定義 
           
        byte[] ServoId = {
                0x10, 0x14, 0x18, 0x02,     // left foot    &  right hand （IKする場合も多少重ねたほうがそれっぽい可能性あり）
                0x0e, 0x12, 0x16, 0x04,     // right foot   &  left hand
                0x0a, 0x0c, 0x1a, 0x1c,     // rolls         
                0x08, 0x06,                 // yaws         
                0x05, 0x07, 0x03            // head （首まわり制御は飾り）
        };
        // 7500を中心に±4000で270° ⇒   296＝10°

        int[] LinkDefLen   = {  2,  4,  2, -1, -2, -4, -2,  1,  0,  0,  0,  0 };  // 屈伸用符号セット
        int[] LinkDefRoll  = {  0,  0,  0,  0,  0,  0,  0,  0,  2,  2, -2, -2 };  // 重心移動用符号セット
        int[] LinkDefPitch = { -1,  0,  1,  1,  1,  0, -1, -1,  0,  0,  0,  0 };  // 前進用符号セット

        private int DuraLen   = 140;    // 角度計算は基本的には table の値ｘDuraXXX x JoyStick倍率で実装。
        private int DuraRoll  =  80;
        private int DuraPitch = 600;
        private int DuraSquat = 200;

                 ：
                中略
                 ：
        
        /// <summary>
        /// 足踏みから歩行へ至る処理を、とりあえずヤッツケで実装してみる。（直値埋め込みまくりで邪悪…）
        /// </summary>
        void MathWalk()
        {
            float X1 = Input.GetAxis("Horizontal");
            float Y1 = Input.GetAxis("Vertical");
            float X2 = Input.GetAxis("Horizontal2");
            float Y2 = Input.GetAxis("Vertical2");

            bool LT = Input.GetButton("LT");

            int[] ServoVal = { 7500, 7500, 7500, 7500, 7500, 7500, 7500, 7500, 7500, 7500, 7500, 7500, 7500, 7500, 7500, 7500, 7500 };
        
            // 姿勢制御（無くても歩ける）

            if (Mathf.Abs(X2) > 0.1)    // 腰ヨー軸もどき
            {
                ServoVal[12] += (int)(600 * X2);
                ServoVal[13] += (int)(600 * X2);
                ServoVal[14] += (int)(600 * X2);    // 首回り

                for (int i = 0; i < 3; i++)
                {
                    ServoVal[i + 0] += (int)((140 * X2) * LinkDefPitch[i + 0]);
                    ServoVal[i + 4] -= (int)((140 * X2) * LinkDefPitch[i + 4]);
                }
            }

            if (Mathf.Abs(Y2) > 0.1)    // 前後姿勢制御
            {
                ServoVal[15] += (int)(300 * X2);    // 首回り
                ServoVal[16] -= (int)(300 * Y2);

                if (Y2 < 0)     // しゃがみ
                {
                    for (int i = 0; i < 8; i++)
                    {
                        ServoVal[i] -= (int)((400 * Y2) * LinkDefLen[i]);
                    }
                }
                else            // 前屈
                {
                    ServoVal[0] += (int)(150 * Y2); // 直値 配列もつけてない。 適当…
                    ServoVal[4] -= (int)(150 * Y2);
                    ServoVal[3] -= (int)(450 * Y2);
                    ServoVal[7] += (int)(450 * Y2);
                }
            }

            // 歩行制御（トリガ―で足踏み開始、半周期で操作可能になる ）

            if (WalkingCondition==0 && LT == true)
            {
                WalkingCondition = 1;
                curTick = 0;
            }

            if (WalkingCondition>0) {

                if (++curTick >= maxTick)
                {
                    curTick = 0;
                    if (WalkingCondition == 1) WalkingCondition = 2;
                    if (LT == false) WalkingCondition = 3;
                }

                float fRateLen = table_sin3[curTick];
                float fRateRoll = table_sin1[curTick];
                float fRateP1 = table_plot1[curTick];
                float fRateP2 = table_plot2[curTick];

                int ratio = 2;

                if (WalkingCondition == 1)      // 歩き始めの足踏み
                {
                    if (curTick < 5)
                        ratio = 1;
                    else
                        WalkingCondition = 2;
                }
                if (WalkingCondition == 3)      // 歩き終わりの足踏み
                {
                    if (curTick < 5)
                        ratio = 1;
                    else
                        WalkingCondition = 0;
                }

                // ROLL処理 ==========
                for (int i = 8; i < 12; i++)
                {
                    ServoVal[i] += (int)((fRateRoll * DuraRoll) * LinkDefRoll[i] * ratio);
                }

                // LEN処理 ==========
                int tmp = 0;
                if (fRateLen < 0)   // 正負で上げる足を違える
                {
                    tmp = 4;
                    fRateLen = -fRateLen;
                }
                for (int i = tmp; i < tmp + 4; i++)
                {
                    ServoVal[i] += (int)((fRateLen * DuraLen) * LinkDefLen[i] * ratio);
                }


                if (WalkingCondition == 2) {        // 足踏み完了後(#2) STICK操作が可能になる

                    // Pitch処理 ==========
                    for (int i = 0; i < 4; i++)
                    {
                        ServoVal[i + 0] += (int)((fRateP1 * DuraPitch) * LinkDefPitch[i + 0] * Y1);
                        ServoVal[i + 4] += (int)((fRateP2 * DuraPitch) * LinkDefPitch[i + 4] * Y1);
                    }

                    // Yaw処理 ==========
                    if ((WalkingSteering != 0 || Mathf.Abs(X1) > 0.1))
                    {
                        int Steering = -(int)(X1 * 250);

                        if (curTick == 2 || curTick == 3 || curTick == 4)
                        {
                            if (Steering > 0)
                                WalkingSteering += Steering;
                            else
                                WalkingSteering = (curTick == 4) ? 0 : WalkingSteering / 2;
                        }
                        if (curTick == 7 || curTick == 8 || curTick == 9)
                        {
                            if (Steering < 0)
                                WalkingSteering -= Steering;
                            else
                                WalkingSteering = (curTick == 9) ? 0 : WalkingSteering / 2;
                        }
                    }

                    ServoVal[12] += WalkingSteering;
                    ServoVal[13] -= WalkingSteering;
                }
            }

            // 0x18コマンドの完成
            string cmd = "38 18 00 " + TickSpeed.ToString("X2");

            for (int i = 0; i < 17; i++)
            {
                byte h = (byte)(ServoVal[i] / 256);
                byte l = (byte)(ServoVal[i] % 256);
                cmd += " " + ServoId[i].ToString("X02") + " " + l.ToString("X02") + " " + h.ToString("X02");
            }
            cmd += " 00";    // XOR予約   memo 長さ不足はエラー10 

            byte[] data = PreMaidUtility.BuildByteDataFromStringOrder(cmd);         // IZMさんライブラリを使う場合はコウなる。
            _serialPort.Write(data, 0, data.Length);
        }
                 ：
                後略
                 ：
    }
}
