/*
* Copyright (c) Contributors, http://opensimulator.org/
* See CONTRIBUTORS.TXT for a full list of copyright holders.
*
* Redistribution and use in source and binary forms, with or without
* modification, are permitted provided that the following conditions are met:
*     * Redistributions of source code must retain the above copyright
*       notice, this list of conditions and the following disclaimer.
*     * Redistributions in binary form must reproduce the above copyright
*       notice, this list of conditions and the following disclaimer in the
*       documentation and/or other materials provided with the distribution.
*     * Neither the name of the OpenSim Project nor the
*       names of its contributors may be used to endorse or promote products
*       derived from this software without specific prior written permission.
*
* THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS AS IS AND ANY
* EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
* WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
* DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
* DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
* (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
* LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
* ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
* (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
* SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
* 
*/

using System;
using System.Collections.Generic;
using System.Runtime.Remoting.Lifetime;
using System.Text;
using System.Threading;
using Axiom.Math;
using libsecondlife;
using OpenSim.Framework;
using OpenSim.Region.Environment.Interfaces;
using OpenSim.Region.Environment.Scenes;
using OpenSim.Region.ScriptEngine.Common;
using OpenSim.Region.ScriptEngine.DotNetEngine.Compiler.LSL;

namespace OpenSim.Region.ScriptEngine.DotNetEngine.Compiler
{
    //
    // !!!IMPORTANT!!!
    //
    // REMEMBER TO UPDATE http://opensimulator.org/wiki/LlFunction_implementation_status
    //

    // Notes:
    // * If any function here needs to execute a LSL event in the script, use instance of "EventQueueManager" in "ScriptEngine".
    // * If any function here needs to do some more advanced stuff like waiting for IO callbacks or similar that takes a long time then use "llSetTimerEvent" function as example.
    //   There is a class called "LSLLongCmdHandler" that is used for long LSL commands.


    /// <summary>
    /// Contains all LSL ll-functions. This class will be in Default AppDomain.
    /// </summary>
    public class LSL_BuiltIn_Commands : MarshalByRefObject, LSL_BuiltIn_Commands_Interface
    {
        private ASCIIEncoding enc = new ASCIIEncoding();
        private ScriptEngine m_ScriptEngine;
        private SceneObjectPart m_host;
        private uint m_localID;
        private LLUUID m_itemID;
        private bool throwErrorOnNotImplemented = true;

        public LSL_BuiltIn_Commands(ScriptEngine ScriptEngine, SceneObjectPart host, uint localID, LLUUID itemID)
        {
            m_ScriptEngine = ScriptEngine;
            m_host = host;
            m_localID = localID;
            m_itemID = itemID;

            //MainLog.Instance.Notice("ScriptEngine", "LSL_BaseClass.Start() called. Hosted by [" + m_host.Name + ":" + m_host.UUID + "@" + m_host.AbsolutePosition + "]");
        }

        private DateTime m_timer = DateTime.Now;
        private string m_state = "default";

        public string State()
        {
            return m_state;
        }

        // Object never expires
        public override Object InitializeLifetimeService()
        {
            //Console.WriteLine("LSL_BuiltIn_Commands: InitializeLifetimeService()");
            //            return null;
            ILease lease = (ILease)base.InitializeLifetimeService();

            if (lease.CurrentState == LeaseState.Initial)
            {
                lease.InitialLeaseTime = TimeSpan.Zero; // TimeSpan.FromMinutes(1);
                //                lease.SponsorshipTimeout = TimeSpan.FromMinutes(2);
                //                lease.RenewOnCallTime = TimeSpan.FromSeconds(2);
            }
            return lease;
        }

        public Scene World
        {
            get { return m_ScriptEngine.World; }
        }

        //These are the implementations of the various ll-functions used by the LSL scripts.
        //starting out, we use the System.Math library for trig functions. - ckrinke 8-14-07
        public double llSin(double f)
        {

            return (double)Math.Sin(f);
        }

        public double llCos(double f)
        {
            return (double)Math.Cos(f);
        }

        public double llTan(double f)
        {
            return (double)Math.Tan(f);
        }

        public double llAtan2(double x, double y)
        {
            return (double)Math.Atan2(y, x);
        }

        public double llSqrt(double f)
        {
            return (double)Math.Sqrt(f);
        }

        public double llPow(double fbase, double fexponent)
        {
            return (double)Math.Pow(fbase, fexponent);
        }

        public int llAbs(int i)
        {
            return (int)Math.Abs(i);
        }

        public double llFabs(double f)
        {
            return (double)Math.Abs(f);
        }

        public double llFrand(double mag)
        {
            lock (Util.RandomClass)
            {
                return Util.RandomClass.NextDouble() * mag;
            }
        }

        public int llFloor(double f)
        {
            return (int)Math.Floor(f);
        }

        public int llCeil(double f)
        {
            return (int)Math.Ceiling(f);
        }

        public int llRound(double f)
        {
            return (int)Math.Round(f, 0);
        }

        //This next group are vector operations involving squaring and square root. ckrinke
        public double llVecMag(LSL_Types.Vector3 v)
        {
            return (v.x * v.x + v.y * v.y + v.z * v.z);
        }

        public LSL_Types.Vector3 llVecNorm(LSL_Types.Vector3 v)
        {
            double mag = v.x * v.x + v.y * v.y + v.z * v.z;
            LSL_Types.Vector3 nor = new LSL_Types.Vector3();
            nor.x = v.x / mag;
            nor.y = v.y / mag;
            nor.z = v.z / mag;
            return nor;
        }

        public double llVecDist(LSL_Types.Vector3 a, LSL_Types.Vector3 b)
        {
            double dx = a.x - b.x;
            double dy = a.y - b.y;
            double dz = a.z - b.z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        //Now we start getting into quaternions which means sin/cos, matrices and vectors. ckrinke
        public LSL_Types.Vector3 llRot2Euler(LSL_Types.Quaternion r)
        {
            //This implementation is from http://lslwiki.net/lslwiki/wakka.php?wakka=LibraryRotationFunctions. ckrinke
            LSL_Types.Quaternion t = new LSL_Types.Quaternion(r.x * r.x, r.y * r.y, r.z * r.z, r.s * r.s);
            double m = (t.x + t.y + t.z + t.s);
            if (m == 0) return new LSL_Types.Vector3();
            double n = 2 * (r.y * r.s + r.x * r.z);
            double p = m * m - n * n;
            if (p > 0)
                return new LSL_Types.Vector3(Math.Atan2(2.0 * (r.x * r.s - r.y * r.z), (-t.x - t.y + t.z + t.s)),
                                             Math.Atan2(n, Math.Sqrt(p)),
                                             Math.Atan2(2.0 * (r.z * r.s - r.x * r.y), (t.x - t.y - t.z + t.s)));
            else if (n > 0)
                return new LSL_Types.Vector3(0.0, Math.PI / 2, Math.Atan2((r.z * r.s + r.x * r.y), 0.5 - t.x - t.z));
            else
                return new LSL_Types.Vector3(0.0, -Math.PI / 2, Math.Atan2((r.z * r.s + r.x * r.y), 0.5 - t.x - t.z));
        }

        public LSL_Types.Quaternion llEuler2Rot(LSL_Types.Vector3 v)
        {
            //this comes from from http://lslwiki.net/lslwiki/wakka.php?wakka=LibraryRotationFunctions but is incomplete as of 8/19/07
            float err = 0.00001f;
            double ax = Math.Sin(v.x / 2);
            double aw = Math.Cos(v.x / 2);
            double by = Math.Sin(v.y / 2);
            double bw = Math.Cos(v.y / 2);
            double cz = Math.Sin(v.z / 2);
            double cw = Math.Cos(v.z / 2);
            LSL_Types.Quaternion a1 = new LSL_Types.Quaternion(0.0, 0.0, cz, cw);
            LSL_Types.Quaternion a2 = new LSL_Types.Quaternion(0.0, by, 0.0, bw);
            LSL_Types.Quaternion a3 = new LSL_Types.Quaternion(ax, 0.0, 0.0, aw);
            LSL_Types.Quaternion a = (a1 * a2) * a3;
            //This multiplication doesnt compile, yet.            a = a1 * a2 * a3;
            LSL_Types.Quaternion b = new LSL_Types.Quaternion(ax * bw * cw + aw * by * cz,
                                                              aw * by * cw - ax * bw * cz, aw * bw * cz + ax * by * cw,
                                                              aw * bw * cw - ax * by * cz);
            LSL_Types.Quaternion c = new LSL_Types.Quaternion();
            //This addition doesnt compile yet c = a + b;
            LSL_Types.Quaternion d = new LSL_Types.Quaternion();
            //This addition doesnt compile yet d = a - b;
            if ((Math.Abs(c.x) > err && Math.Abs(d.x) > err) ||
                (Math.Abs(c.y) > err && Math.Abs(d.y) > err) ||
                (Math.Abs(c.z) > err && Math.Abs(d.z) > err) ||
                (Math.Abs(c.s) > err && Math.Abs(d.s) > err))
            {
                return b;
                //return a new Quaternion that is null until I figure this out
                //                return b;
                //            return a;
            }
            return a;
        }

        public LSL_Types.Quaternion llAxes2Rot(LSL_Types.Vector3 fwd, LSL_Types.Vector3 left, LSL_Types.Vector3 up)
        {
            return new LSL_Types.Quaternion();
        }

        public LSL_Types.Vector3 llRot2Fwd(LSL_Types.Quaternion r)
        {
            return new LSL_Types.Vector3();
        }

        public LSL_Types.Vector3 llRot2Left(LSL_Types.Quaternion r)
        {
            return new LSL_Types.Vector3();
        }

        public LSL_Types.Vector3 llRot2Up(LSL_Types.Quaternion r)
        {
            return new LSL_Types.Vector3();
        }
        public LSL_Types.Quaternion llRotBetween(LSL_Types.Vector3 a, LSL_Types.Vector3 b)
        {
            //A and B should both be normalized

            double dotProduct = LSL_Types.Vector3.Dot(a, b);
            LSL_Types.Vector3 crossProduct = LSL_Types.Vector3.Cross(a, b);
            double magProduct = LSL_Types.Vector3.Mag(a) * LSL_Types.Vector3.Mag(b);
            double angle = Math.Acos(dotProduct / magProduct);
            LSL_Types.Vector3 axis = LSL_Types.Vector3.Norm(crossProduct);
            double s = Math.Sin(angle / 2);

            return new LSL_Types.Quaternion(axis.x * s, axis.y * s, axis.z * s, (float)Math.Cos(angle / 2));
        }
        public void llWhisper(int channelID, string text)
        {
            World.SimChat(Helpers.StringToField(text),
                          ChatTypeEnum.Whisper, channelID, m_host.AbsolutePosition, m_host.Name, m_host.UUID);

            IWorldComm wComm = m_ScriptEngine.World.RequestModuleInterface<IWorldComm>();
            wComm.DeliverMessage(m_host.UUID.ToString(), ChatTypeEnum.Whisper, channelID, m_host.Name, text);
        }

        public void llSay(int channelID, string text)
        {
            World.SimChat(Helpers.StringToField(text),
                          ChatTypeEnum.Say, channelID, m_host.AbsolutePosition, m_host.Name, m_host.UUID);

            IWorldComm wComm = m_ScriptEngine.World.RequestModuleInterface<IWorldComm>();
            wComm.DeliverMessage(m_host.UUID.ToString(), ChatTypeEnum.Say, channelID, m_host.Name, text);
        }

        public void llShout(int channelID, string text)
        {
            World.SimChat(Helpers.StringToField(text),
                          ChatTypeEnum.Shout, channelID, m_host.AbsolutePosition, m_host.Name, m_host.UUID);

            IWorldComm wComm = m_ScriptEngine.World.RequestModuleInterface<IWorldComm>();
            wComm.DeliverMessage(m_host.UUID.ToString(), ChatTypeEnum.Shout, channelID, m_host.Name, text);
        }

        public int llListen(int channelID, string name, string ID, string msg)
        {
            if (ID == "")
            {
                ID = LLUUID.Zero.ToString();
            }
            IWorldComm wComm = m_ScriptEngine.World.RequestModuleInterface<IWorldComm>();
            return wComm.Listen(m_localID, m_itemID, m_host.UUID, channelID, name, ID, msg);
        }

        public void llListenControl(int number, int active)
        {
            IWorldComm wComm = m_ScriptEngine.World.RequestModuleInterface<IWorldComm>();
            wComm.ListenControl(number, active);
        }

        public void llListenRemove(int number)
        {
            IWorldComm wComm = m_ScriptEngine.World.RequestModuleInterface<IWorldComm>();
            wComm.ListenRemove(number);
        }

        public void llSensor(string name, string id, int type, double range, double arc)
        {
            NotImplemented("llSensor");
            return;
        }

        public void llSensorRepeat(string name, string id, int type, double range, double arc, double rate)
        {
            NotImplemented("llSensorRepeat");
            return;
        }

        public void llSensorRemove()
        {
            NotImplemented("llSensorRemove");
            return;
        }

        public string llDetectedName(int number)
        {
            NotImplemented("llDetectedName");
            return "";
        }

        public string llDetectedKey(int number)
        {
            NotImplemented("llDetectedKey");
            return "";
        }

        public string llDetectedOwner(int number)
        {
            NotImplemented("llDetectedOwner");
            return "";
        }

        public int llDetectedType(int number)
        {
            NotImplemented("llDetectedType");
            return 0;
        }

        public LSL_Types.Vector3 llDetectedPos(int number)
        {
            NotImplemented("llDetectedPos");
            return new LSL_Types.Vector3();
        }

        public LSL_Types.Vector3 llDetectedVel(int number)
        {
            NotImplemented("llDetectedVel");
            return new LSL_Types.Vector3();
        }

        public LSL_Types.Vector3 llDetectedGrab(int number)
        {
            NotImplemented("llDetectedGrab");
            return new LSL_Types.Vector3();
        }

        public LSL_Types.Quaternion llDetectedRot(int number)
        {
            NotImplemented("llDetectedRot");
            return new LSL_Types.Quaternion();
        }

        public int llDetectedGroup(int number)
        {
            NotImplemented("llDetectedGroup");
            return 0;
        }

        public int llDetectedLinkNumber(int number)
        {
            NotImplemented("llDetectedLinkNumber");
            return 0;
        }

        public void llDie()
        {
            World.DeleteSceneObjectGroup(m_host.ParentGroup);
            return;
        }

        public double llGround(LSL_Types.Vector3 offset)
        {
            int x = (int)(m_host.AbsolutePosition.X + offset.x);
            int y = (int)(m_host.AbsolutePosition.Y + offset.y);
            return World.GetLandHeight(x, y);
        }

        public double llCloud(LSL_Types.Vector3 offset)
        {
            NotImplemented("llCloud");
            return 0;
        }

        public LSL_Types.Vector3 llWind(LSL_Types.Vector3 offset)
        {
            NotImplemented("llWind");
            return new LSL_Types.Vector3();
        }

        public void llSetStatus(int status, int value)
        {
            NotImplemented("llSetStatus");
            return;
        }

        public int llGetStatus(int status)
        {
            NotImplemented("llGetStatus");
            return 0;
        }

        public void llSetScale(LSL_Types.Vector3 scale)
        {
            // TODO: this needs to trigger a persistance save as well
            LLVector3 tmp = m_host.Scale;
            tmp.X = (float)scale.x;
            tmp.Y = (float)scale.y;
            tmp.Z = (float)scale.z;
            m_host.Scale = tmp;
            m_host.SendFullUpdateToAllClients();
            return;
        }

        public LSL_Types.Vector3 llGetScale()
        {
            return new LSL_Types.Vector3(m_host.Scale.X, m_host.Scale.Y, m_host.Scale.Z);
        }

        public void llSetColor(LSL_Types.Vector3 color, int face)
        {
            LLObject.TextureEntry tex = m_host.Shape.Textures;
            LLColor texcolor;
            if (face > -1)
            {
                texcolor = tex.CreateFace((uint)face).RGBA;
                texcolor.R = (float)Math.Abs(color.x - 1);
                texcolor.G = (float)Math.Abs(color.y - 1);
                texcolor.B = (float)Math.Abs(color.z - 1);
                tex.FaceTextures[face].RGBA = texcolor;
                m_host.UpdateTexture(tex);
                return;
            }
            else if (face == -1)
            {
                for (uint i = 0; i < 32; i++)
                {
                    if (tex.FaceTextures[i] != null)
                    {
                        texcolor = tex.FaceTextures[i].RGBA;
                        texcolor.R = (float)Math.Abs(color.x - 1);
                        texcolor.G = (float)Math.Abs(color.y - 1);
                        texcolor.B = (float)Math.Abs(color.z - 1);
                        tex.FaceTextures[i].RGBA = texcolor;
                    }
                    texcolor = tex.DefaultTexture.RGBA;
                    texcolor.R = (float)Math.Abs(color.x - 1);
                    texcolor.G = (float)Math.Abs(color.y - 1);
                    texcolor.B = (float)Math.Abs(color.z - 1);
                    tex.DefaultTexture.RGBA = texcolor;
                }
                m_host.UpdateTexture(tex);
                return;
            }
            NotImplemented("llSetColor");
            return;
        }

        public double llGetAlpha(int face)
        {
            LLObject.TextureEntry tex = m_host.Shape.Textures;
            if (face == -1) // TMP: Until we can determine number of sides, ALL_SIDES (-1) will return default color
            {
                return (double)((tex.DefaultTexture.RGBA.A * 255) / 255);
            }
            if (face > -1)
            {
                return (double)((tex.GetFace((uint)face).RGBA.A * 255) / 255);
            }
            return 0;
        }

        public void llSetAlpha(double alpha, int face)
        {
            LLObject.TextureEntry tex = m_host.Shape.Textures;
            LLColor texcolor;
            if (face > -1)
            {
                texcolor = tex.CreateFace((uint)face).RGBA;
                texcolor.A = (float)Math.Abs(alpha - 1);
                tex.FaceTextures[face].RGBA = texcolor;
                m_host.UpdateTexture(tex);
                return;
            }
            else if (face == -1)
            {
                for (int i = 0; i < 32; i++)
                {
                    if (tex.FaceTextures[i] != null)
                    {
                        texcolor = tex.FaceTextures[i].RGBA;
                        texcolor.A = (float)Math.Abs(alpha - 1);
                        tex.FaceTextures[i].RGBA = texcolor;
                    }
                }
                texcolor = tex.DefaultTexture.RGBA;
                texcolor.A = (float)Math.Abs(alpha - 1);
                tex.DefaultTexture.RGBA = texcolor;
                m_host.UpdateTexture(tex);
                return;
            }
            NotImplemented("llSetAlpha");
            return;
        }

        public LSL_Types.Vector3 llGetColor(int face)
        {
            LLObject.TextureEntry tex = m_host.Shape.Textures;
            LLColor texcolor;
            LSL_Types.Vector3 rgb;
            if (face == -1) // TMP: Until we can determine number of sides, ALL_SIDES (-1) will return default color
            {
                texcolor = tex.DefaultTexture.RGBA;
                rgb.x = (255 - (texcolor.R * 255)) / 255;
                rgb.y = (255 - (texcolor.G * 255)) / 255;
                rgb.z = (255 - (texcolor.B * 255)) / 255;
                return rgb;
            }
            if (face > -1)
            {
                texcolor = tex.GetFace((uint)face).RGBA;
                rgb.x = (255 - (texcolor.R * 255)) / 255;
                rgb.y = (255 - (texcolor.G * 255)) / 255;
                rgb.z = (255 - (texcolor.B * 255)) / 255;
                return rgb;
            }
            NotImplemented("llGetColor");
            return new LSL_Types.Vector3();
        }

        public void llSetTexture(string texture, int face)
        {
            LLObject.TextureEntry tex = m_host.Shape.Textures;

            if (face > -1)
            {
                LLObject.TextureEntryFace texface = tex.CreateFace((uint)face);
                texface.TextureID = new LLUUID(texture);
                tex.FaceTextures[face] = texface;
                m_host.UpdateTexture(tex);
                return;
            }
            else if (face == -1)
            {
                for (uint i = 0; i < 32; i++)
                {
                    if (tex.FaceTextures[i] != null)
                    {
                        tex.FaceTextures[i].TextureID = new LLUUID(texture);
                    }
                }
                tex.DefaultTexture.TextureID = new LLUUID(texture);
                m_host.UpdateTexture(tex);
                return;
            }
            NotImplemented("llSetTexture");
            return;
        }

        public void llScaleTexture(double u, double v, int face)
        {
            LLObject.TextureEntry tex = m_host.Shape.Textures;
            if (face > -1)
            {
                LLObject.TextureEntryFace texface = tex.CreateFace((uint)face);
                texface.RepeatU = (float)u;
                texface.RepeatV = (float)v;
                tex.FaceTextures[face] = texface;
                m_host.UpdateTexture(tex);
                return;
            }
            if (face == -1)
            {
                for (int i = 0; i < 32; i++)
                {
                    if (tex.FaceTextures[i] != null)
                    {
                        tex.FaceTextures[i].RepeatU = (float)u;
                        tex.FaceTextures[i].RepeatV = (float)v;
                    }
                }
                tex.DefaultTexture.RepeatU = (float)u;
                tex.DefaultTexture.RepeatV = (float)v;
                m_host.UpdateTexture(tex);
                return;
            }
            NotImplemented("llScaleTexture");
            return;
        }

        public void llOffsetTexture(double u, double v, int face)
        {
            LLObject.TextureEntry tex = m_host.Shape.Textures;
            if (face > -1)
            {
                LLObject.TextureEntryFace texface = tex.CreateFace((uint)face);
                texface.OffsetU = (float)u;
                texface.OffsetV = (float)v;
                tex.FaceTextures[face] = texface;
                m_host.UpdateTexture(tex);
                return;
            }
            if (face == -1)
            {
                for (int i = 0; i < 32; i++)
                {
                    if (tex.FaceTextures[i] != null)
                    {
                        tex.FaceTextures[i].OffsetU = (float)u;
                        tex.FaceTextures[i].OffsetV = (float)v;
                    }
                }
                tex.DefaultTexture.OffsetU = (float)u;
                tex.DefaultTexture.OffsetV = (float)v;
                m_host.UpdateTexture(tex);
                return;
            }
            NotImplemented("llOffsetTexture");
            return;
        }

        public void llRotateTexture(double rotation, int face)
        {
            LLObject.TextureEntry tex = m_host.Shape.Textures;
            if (face > -1)
            {
                LLObject.TextureEntryFace texface = tex.CreateFace((uint)face);
                texface.Rotation = (float)rotation;
                tex.FaceTextures[face] = texface;
                m_host.UpdateTexture(tex);
                return;
            }
            if (face == -1)
            {
                for (int i = 0; i < 32; i++)
                {
                    if (tex.FaceTextures[i] != null)
                    {
                        tex.FaceTextures[i].Rotation = (float)rotation;
                    }
                }
                tex.DefaultTexture.Rotation = (float)rotation;
                m_host.UpdateTexture(tex);
                return;
            }
            NotImplemented("llRotateTexture");
            return;
        }

        public string llGetTexture(int face)
        {
            LLObject.TextureEntry tex = m_host.Shape.Textures;
            if (face == -1)
            {
                face = 0;
            }
            if (face > -1)
            {
                LLObject.TextureEntryFace texface;
                texface = tex.GetFace((uint)face);
                return texface.TextureID.ToString();
            }
            NotImplemented("llGetTexture");
            return "";
        }

        public void llSetPos(LSL_Types.Vector3 pos)
        {
            if (m_host.ParentID != 0)
            {
                m_host.UpdateOffSet(new LLVector3((float)pos.x, (float)pos.y, (float)pos.z));
            }
            else
            {
                m_host.UpdateGroupPosition(new LLVector3((float)pos.x, (float)pos.y, (float)pos.z));
            }
        }

        public LSL_Types.Vector3 llGetPos()
        {
            return new LSL_Types.Vector3(m_host.AbsolutePosition.X,
                                         m_host.AbsolutePosition.Y,
                                         m_host.AbsolutePosition.Z);
        }

        public LSL_Types.Vector3 llGetLocalPos()
        {
            if (m_host.ParentID != 0)
            {
                return new LSL_Types.Vector3(m_host.OffsetPosition.X,
                                             m_host.OffsetPosition.Y,
                                             m_host.OffsetPosition.Z);
            }
            else
            {
                return new LSL_Types.Vector3(m_host.AbsolutePosition.X,
                                             m_host.AbsolutePosition.Y,
                                             m_host.AbsolutePosition.Z);
            }
        }

        public void llSetRot(LSL_Types.Quaternion rot)
        {
            m_host.UpdateRotation(new LLQuaternion((float)rot.x, (float)rot.y, (float)rot.z, (float)rot.s));
        }

        public LSL_Types.Quaternion llGetRot()
        {
            LLQuaternion q = m_host.RotationOffset;
            return new LSL_Types.Quaternion(q.X, q.Y, q.Z, q.W);
        }

        public LSL_Types.Quaternion llGetLocalRot()
        {
            return new LSL_Types.Quaternion(m_host.RotationOffset.X, m_host.RotationOffset.Y, m_host.RotationOffset.Z, m_host.RotationOffset.W);
        }

        public void llSetForce(LSL_Types.Vector3 force, int local)
        {
            NotImplemented("llSetForce");
        }

        public LSL_Types.Vector3 llGetForce()
        {
            NotImplemented("llGetForce");
            return new LSL_Types.Vector3();
        }

        public int llTarget(LSL_Types.Vector3 position, double range)
        {
            NotImplemented("llTarget");
            return 0;
        }

        public void llTargetRemove(int number)
        {
            NotImplemented("llTargetRemove");
        }

        public int llRotTarget(LSL_Types.Quaternion rot, double error)
        {
            NotImplemented("llRotTarget");
            return 0;
        }

        public void llRotTargetRemove(int number)
        {
            NotImplemented("llRotTargetRemove");
        }

        public void llMoveToTarget(LSL_Types.Vector3 target, double tau)
        {
            NotImplemented("llMoveToTarget");
        }

        public void llStopMoveToTarget()
        {
            NotImplemented("llStopMoveToTarget");
        }

        public void llApplyImpulse(LSL_Types.Vector3 force, int local)
        {
            NotImplemented("llApplyImpulse");
        }

        public void llApplyRotationalImpulse(LSL_Types.Vector3 force, int local)
        {
            NotImplemented("llApplyRotationalImpulse");
        }

        public void llSetTorque(LSL_Types.Vector3 torque, int local)
        {
            NotImplemented("llSetTorque");
        }

        public LSL_Types.Vector3 llGetTorque()
        {
            NotImplemented("llGetTorque");
            return new LSL_Types.Vector3();
        }

        public void llSetForceAndTorque(LSL_Types.Vector3 force, LSL_Types.Vector3 torque, int local)
        {
            NotImplemented("llSetForceAndTorque");
        }

        public LSL_Types.Vector3 llGetVel()
        {
            return new LSL_Types.Vector3(m_host.Velocity.X, m_host.Velocity.Y, m_host.Velocity.Z);
        }

        public LSL_Types.Vector3 llGetAccel()
        {
            return new LSL_Types.Vector3(m_host.Acceleration.X, m_host.Acceleration.Y, m_host.Acceleration.Z);
        }

        public LSL_Types.Vector3 llGetOmega()
        {
            NotImplemented("llGetOmega");
            return new LSL_Types.Vector3();
        }

        public double llGetTimeOfDay()
        {
            NotImplemented("llGetTimeOfDay");
            return 0;
        }

        public double llGetWallclock()
        {
            return DateTime.Now.TimeOfDay.TotalSeconds;
        }

        public double llGetTime()
        {
            TimeSpan ScriptTime = DateTime.Now - m_timer;
            return (double)(ScriptTime.TotalMilliseconds / 1000);
        }

        public void llResetTime()
        {
            m_timer = DateTime.Now;
        }

        public double llGetAndResetTime()
        {
            TimeSpan ScriptTime = DateTime.Now - m_timer;
            m_timer = DateTime.Now;
            return (double)(ScriptTime.TotalMilliseconds / 1000);
        }

        public void llSound()
        {
            NotImplemented("llSound");
        }

        public void llPlaySound(string sound, double volume)
        {
            NotImplemented("llPlaySound");
        }

        public void llLoopSound(string sound, double volume)
        {
            NotImplemented("llLoopSound");
        }

        public void llLoopSoundMaster(string sound, double volume)
        {
            NotImplemented("llLoopSoundMaster");
        }

        public void llLoopSoundSlave(string sound, double volume)
        {
            NotImplemented("llLoopSoundSlave");
        }

        public void llPlaySoundSlave(string sound, double volume)
        {
            NotImplemented("llPlaySoundSlave");
        }

        public void llTriggerSound(string sound, double volume)
        {
            NotImplemented("llTriggerSound");
        }

        public void llStopSound()
        {
            NotImplemented("llStopSound");
        }

        public void llPreloadSound(string sound)
        {
            NotImplemented("llPreloadSound");
        }

        public string llGetSubString(string src, int start, int end)
        {
            return src.Substring(start, end);
        }

        public string llDeleteSubString(string src, int start, int end)
        {
            return src.Remove(start, end - start);
        }

        public string llInsertString(string dst, int position, string src)
        {
            return dst.Insert(position, src);
        }

        public string llToUpper(string src)
        {
            return src.ToUpper();
        }

        public string llToLower(string src)
        {
            return src.ToLower();
        }

        public int llGiveMoney(string destination, int amount)
        {
            NotImplemented("llGiveMoney");
            return 0;
        }

        public void llMakeExplosion()
        {
            NotImplemented("llMakeExplosion");
        }

        public void llMakeFountain()
        {
            NotImplemented("llMakeFountain");
        }

        public void llMakeSmoke()
        {
            NotImplemented("llMakeSmoke");
        }

        public void llMakeFire()
        {
            NotImplemented("llMakeFire");
        }

        public void llRezObject(string inventory, LSL_Types.Vector3 pos, LSL_Types.Quaternion rot, int param)
        {
            NotImplemented("llRezObject");
        }

        public void llLookAt(LSL_Types.Vector3 target, double strength, double damping)
        {
            NotImplemented("llLookAt");
        }

        public void llStopLookAt()
        {
            NotImplemented("llStopLookAt");
        }

        public void llSetTimerEvent(double sec)
        {
            // Setting timer repeat
            m_ScriptEngine.m_LSLLongCmdHandler.SetTimerEvent(m_localID, m_itemID, sec);
        }

        public void llSleep(double sec)
        {
            Thread.Sleep((int)(sec * 1000));
        }

        public double llGetMass()
        {
            return m_host.GetMass();
        }

        public void llCollisionFilter(string name, string id, int accept)
        {
            NotImplemented("llCollisionFilter");
        }

        public void llTakeControls(int controls, int accept, int pass_on)
        {
            NotImplemented("llTakeControls");
        }

        public void llReleaseControls()
        {
            NotImplemented("llReleaseControls");
        }

        public void llAttachToAvatar(int attachment)
        {
            NotImplemented("llAttachToAvatar");
        }

        public void llDetachFromAvatar()
        {
            NotImplemented("llDetachFromAvatar");
        }

        public void llTakeCamera()
        {
            NotImplemented("llTakeCamera");
        }

        public void llReleaseCamera()
        {
            NotImplemented("llReleaseCamera");
        }

        public string llGetOwner()
        {
            return m_host.ObjectOwner.ToString();
        }

        public void llInstantMessage(string user, string message)
        {
            NotImplemented("llInstantMessage");

            // We may be able to use ClientView.SendInstantMessage here, but we need a client instance.
            // InstantMessageModule.OnInstantMessage searches through a list of scenes for a client matching the toAgent,
            // but I don't think we have a list of scenes available from here.
            // (We also don't want to duplicate the code in OnInstantMessage if we can avoid it.)

            // TODO: figure out values for client, fromSession, and imSessionID
            // client.SendInstantMessage(m_host.UUID, fromSession, message, user, imSessionID, m_host.Name, AgentManager.InstantMessageDialog.MessageFromAgent, (uint)Util.UnixTimeSinceEpoch());
        }

        public void llEmail(string address, string subject, string message)
        {
            NotImplemented("llEmail");
        }

        public void llGetNextEmail(string address, string subject)
        {
            NotImplemented("llGetNextEmail");
        }

        public string llGetKey()
        {
            return m_host.UUID.ToString();
        }

        public void llSetBuoyancy(double buoyancy)
        {
            NotImplemented("llSetBuoyancy");
        }

        public void llSetHoverHeight(double height, int water, double tau)
        {
            NotImplemented("llSetHoverHeight");
        }

        public void llStopHover()
        {
            NotImplemented("llStopHover");
        }

        public void llMinEventDelay(double delay)
        {
            NotImplemented("llMinEventDelay");
        }

        public void llSoundPreload()
        {
            NotImplemented("llSoundPreload");
        }

        public void llRotLookAt(LSL_Types.Quaternion target, double strength, double damping)
        {
            NotImplemented("llRotLookAt");
        }

        public int llStringLength(string str)
        {
            if (str.Length > 0)
            {
                return str.Length;
            }
            else
            {
                return 0;
            }
        }

        public void llStartAnimation(string anim)
        {
            NotImplemented("llStartAnimation");
        }

        public void llStopAnimation(string anim)
        {
            NotImplemented("llStopAnimation");
        }

        public void llPointAt()
        {
            NotImplemented("llPointAt");
        }

        public void llStopPointAt()
        {
            NotImplemented("llStopPointAt");
        }

        public void llTargetOmega(LSL_Types.Vector3 axis, double spinrate, double gain)
        {
            m_host.RotationalVelocity = new LLVector3((float)(axis.x * spinrate), (float)(axis.y * spinrate), (float)(axis.z * spinrate));
            m_host.AngularVelocity = new LLVector3((float)(axis.x * spinrate), (float)(axis.y * spinrate), (float)(axis.z * spinrate));
            m_host.ScheduleTerseUpdate();
            m_host.SendTerseUpdateToAllClients();
            //NotImplemented("llTargetOmega");
        }

        public int llGetStartParameter()
        {
            NotImplemented("llGetStartParameter");
            return 0;
        }

        public void llGodLikeRezObject(string inventory, LSL_Types.Vector3 pos)
        {
            NotImplemented("llGodLikeRezObject");
        }

        public void llRequestPermissions(string agent, int perm)
        {
            NotImplemented("llRequestPermissions");
        }

        public string llGetPermissionsKey()
        {
            NotImplemented("llGetPermissionsKey");
            return "";
        }

        public int llGetPermissions()
        {
            NotImplemented("llGetPermissions");
            return 0;
        }

        public int llGetLinkNumber()
        {
            return m_host.LinkNum;
        }

        public void llSetLinkColor(int linknumber, LSL_Types.Vector3 color, int face)
        {
            SceneObjectPart part = m_host.ParentGroup.GetLinkNumPart(linknumber);
            if (linknumber > -1)
            {
                LLObject.TextureEntry tex = part.Shape.Textures;
                LLColor texcolor;
                if (face > -1)
                {
                    texcolor = tex.CreateFace((uint)face).RGBA;
                    texcolor.R = (float)Math.Abs(color.x - 1);
                    texcolor.G = (float)Math.Abs(color.y - 1);
                    texcolor.B = (float)Math.Abs(color.z - 1);
                    tex.FaceTextures[face].RGBA = texcolor;
                    part.UpdateTexture(tex);
                    return;
                }
                else if (face == -1)
                {
                    texcolor = tex.DefaultTexture.RGBA;
                    texcolor.R = (float)Math.Abs(color.x - 1);
                    texcolor.G = (float)Math.Abs(color.y - 1);
                    texcolor.B = (float)Math.Abs(color.z - 1);
                    tex.DefaultTexture.RGBA = texcolor;
                    for (uint i = 0; i < 32; i++)
                    {
                        if (tex.FaceTextures[i] != null)
                        {
                            texcolor = tex.FaceTextures[i].RGBA;
                            texcolor.R = (float)Math.Abs(color.x - 1);
                            texcolor.G = (float)Math.Abs(color.y - 1);
                            texcolor.B = (float)Math.Abs(color.z - 1);
                            tex.FaceTextures[i].RGBA = texcolor;
                        }
                    }
                    texcolor = tex.DefaultTexture.RGBA;
                    texcolor.R = (float)Math.Abs(color.x - 1);
                    texcolor.G = (float)Math.Abs(color.y - 1);
                    texcolor.B = (float)Math.Abs(color.z - 1);
                    tex.DefaultTexture.RGBA = texcolor;
                    part.UpdateTexture(tex);
                    return;
                }
                return;
            }
            else if (linknumber == -1)
            {
                int num = m_host.ParentGroup.PrimCount;
                for (int w = 0; w < num; w++)
                {
                    linknumber = w;
                    part = m_host.ParentGroup.GetLinkNumPart(linknumber);
                    LLObject.TextureEntry tex = part.Shape.Textures;
                    LLColor texcolor;
                    if (face > -1)
                    {
                        texcolor = tex.CreateFace((uint)face).RGBA;
                        texcolor.R = (float)Math.Abs(color.x - 1);
                        texcolor.G = (float)Math.Abs(color.y - 1);
                        texcolor.B = (float)Math.Abs(color.z - 1);
                        tex.FaceTextures[face].RGBA = texcolor;
                        part.UpdateTexture(tex);
                    }
                    else if (face == -1)
                    {
                        texcolor = tex.DefaultTexture.RGBA;
                        texcolor.R = (float)Math.Abs(color.x - 1);
                        texcolor.G = (float)Math.Abs(color.y - 1);
                        texcolor.B = (float)Math.Abs(color.z - 1);
                        tex.DefaultTexture.RGBA = texcolor;
                        for (uint i = 0; i < 32; i++)
                        {
                            if (tex.FaceTextures[i] != null)
                            {
                                texcolor = tex.FaceTextures[i].RGBA;
                                texcolor.R = (float)Math.Abs(color.x - 1);
                                texcolor.G = (float)Math.Abs(color.y - 1);
                                texcolor.B = (float)Math.Abs(color.z - 1);
                                tex.FaceTextures[i].RGBA = texcolor;
                            }
                        }
                        texcolor = tex.DefaultTexture.RGBA;
                        texcolor.R = (float)Math.Abs(color.x - 1);
                        texcolor.G = (float)Math.Abs(color.y - 1);
                        texcolor.B = (float)Math.Abs(color.z - 1);
                        tex.DefaultTexture.RGBA = texcolor;
                        part.UpdateTexture(tex);
                    }
                }
                return;
            }
            else
            {
                NotImplemented("llSetLinkColor");
            }
        }

        public void llCreateLink(string target, int parent)
        {
            NotImplemented("llCreateLink");
        }

        public void llBreakLink(int linknum)
        {
            NotImplemented("llBreakLink");
        }

        public void llBreakAllLinks()
        {
            NotImplemented("llBreakAllLinks");
        }

        public string llGetLinkKey(int linknum)
        {
            SceneObjectPart part = m_host.ParentGroup.GetLinkNumPart(linknum);
            if (part != null)
            {
                return part.UUID.ToString();
            }
            else
            {
                return "00000000-0000-0000-0000-000000000000";
            }
        }

        public string llGetLinkName(int linknum)
        {
            SceneObjectPart part = m_host.ParentGroup.GetLinkNumPart(linknum);
            if (part != null)
            {
                return part.Name;
            }
            else
            {
                return "00000000-0000-0000-0000-000000000000";
            }
        }

        public int llGetInventoryNumber(int type)
        {
            NotImplemented("llGetInventoryNumber");
            return 0;
        }

        public string llGetInventoryName(int type, int number)
        {
            NotImplemented("llGetInventoryName");
            return "";
        }

        public void llSetScriptState(string name, int run)
        {
            NotImplemented("llSetScriptState");
        }

        public double llGetEnergy()
        {
            return 1.0f;
        }

        public void llGiveInventory(string destination, string inventory)
        {
            NotImplemented("llGiveInventory");
        }

        public void llRemoveInventory(string item)
        {
            NotImplemented("llRemoveInventory");
        }

        public void llSetText(string text, LSL_Types.Vector3 color, double alpha)
        {
            Vector3 av3 = new Vector3((float)color.x, (float)color.y, (float)color.z);
            m_host.SetText(text, av3, alpha);
        }

        public double llWater(LSL_Types.Vector3 offset)
        {
            return World.RegionInfo.EstateSettings.waterHeight;
        }

        public void llPassTouches(int pass)
        {
            NotImplemented("llPassTouches");
        }

        public string llRequestAgentData(string id, int data)
        {
            NotImplemented("llRequestAgentData");
            return "";
        }

        public string llRequestInventoryData(string name)
        {
            NotImplemented("llRequestInventoryData");
            return "";
        }

        public void llSetDamage(double damage)
        {
            NotImplemented("llSetDamage");
        }

        public void llTeleportAgentHome(string agent)
        {
            NotImplemented("llTeleportAgentHome");
        }

        public void llModifyLand(int action, int brush)
        {
            double dsize;
            if (World.PermissionsMngr.CanTerraform(m_host.OwnerID, new LLVector3(m_host.AbsolutePosition.X, m_host.AbsolutePosition.Y, 0)))
            {
                switch (brush)
                {
                    case 1:
                        dsize = 2;
                        break;
                    case 2:
                        dsize = 4;
                        break;
                    case 3:
                        dsize = 8;
                        break;
                    default:
                        if (brush < 0)
                        {
                            dsize = (double)(-1 * brush);
                        }
                        else
                        {
                            LSLError("Invalid brush size");
                            dsize = 0; // Should cease execution, but get unassigned local variable dsize on compile.
                        }
                        break;
                }
                switch (action)
                {
                    case 0:
                        if (World.Terrain.GetHeight((int)m_host.AbsolutePosition.X, (int)m_host.AbsolutePosition.Y) < m_host.AbsolutePosition.Z)
                        {
                            World.Terrain.FlattenTerrain(m_host.AbsolutePosition.X, m_host.AbsolutePosition.Y, dsize, 1);
                        }
                        break;
                    case 1:
                        if (World.Terrain.GetHeight((int)m_host.AbsolutePosition.X, (int)m_host.AbsolutePosition.Y) < (double)m_host.AbsolutePosition.Z)
                        {
                            World.Terrain.RaiseTerrain(m_host.AbsolutePosition.X, m_host.AbsolutePosition.Y, dsize, 0.1);
                        }
                        break;
                    case 2:
                        if (World.Terrain.GetHeight((int)m_host.AbsolutePosition.X, (int)m_host.AbsolutePosition.Y) > 0)
                        {
                            World.Terrain.LowerTerrain(m_host.AbsolutePosition.X, m_host.AbsolutePosition.Y, dsize, 1);
                        }
                        break;
                    case 3:
                        World.Terrain.SmoothTerrain(m_host.AbsolutePosition.X, m_host.AbsolutePosition.Y, dsize, 1);
                        break;
                    case 4:
                        World.Terrain.NoiseTerrain(m_host.AbsolutePosition.X, m_host.AbsolutePosition.Y, dsize, 1);
                        break;
                    case 5:
                        World.Terrain.RevertTerrain(m_host.AbsolutePosition.X, m_host.AbsolutePosition.Y, dsize, 1);
                        break;
                    default:
                        break;
                }
            }
        }

        public void llCollisionSound(string impact_sound, double impact_volume)
        {
            NotImplemented("llCollisionSound");
        }

        public void llCollisionSprite(string impact_sprite)
        {
            NotImplemented("llCollisionSprite");
        }

        public string llGetAnimation(string id)
        {
            NotImplemented("llGetAnimation");
            return "";
        }

        public void llResetScript()
        {
            m_ScriptEngine.m_ScriptManager.ResetScript(m_localID, m_itemID);
        }

        public void llMessageLinked(int linknum, int num, string str, string id)
        {
        }

        public void llPushObject(string target, LSL_Types.Vector3 impulse, LSL_Types.Vector3 ang_impulse, int local)
        {
        }

        public void llPassCollisions(int pass)
        {
        }

        public string llGetScriptName()
        {
            return "";
        }

        public int llGetNumberOfSides()
        {
            return 0;
        }

        public LSL_Types.Quaternion llAxisAngle2Rot(LSL_Types.Vector3 axis, double angle)
        {
            return new LSL_Types.Quaternion();
        }

        public LSL_Types.Vector3 llRot2Axis(LSL_Types.Quaternion rot)
        {
            return new LSL_Types.Vector3();
        }

        public void llRot2Angle()
        {
        }

        public double llAcos(double val)
        {
            return (double)Math.Acos(val);
        }

        public double llAsin(double val)
        {
            return (double)Math.Asin(val);
        }

        public double llAngleBetween(LSL_Types.Quaternion a, LSL_Types.Quaternion b)
        {
            return 0;
        }

        public string llGetInventoryKey(string name)
        {
            return "";
        }

        public void llAllowInventoryDrop(int add)
        {
        }

        public LSL_Types.Vector3 llGetSunDirection()
        {
            return new LSL_Types.Vector3();
        }

        public LSL_Types.Vector3 llGetTextureOffset(int face)
        {
            LLObject.TextureEntry tex = m_host.Shape.Textures;
            LSL_Types.Vector3 offset;
            if (face == -1)
            {
                face = 0;
            }
            offset.x = tex.GetFace((uint)face).OffsetU;
            offset.y = tex.GetFace((uint)face).OffsetV;
            offset.z = 0.0;
            return offset;
        }

        public LSL_Types.Vector3 llGetTextureScale(int side)
        {
            LLObject.TextureEntry tex = m_host.Shape.Textures;
            LSL_Types.Vector3 scale;
            if (side == -1)
            {
                side = 0;
            }
            scale.x = tex.GetFace((uint)side).RepeatU;
            scale.y = tex.GetFace((uint)side).RepeatV;
            scale.z = 0.0;
            return scale;
        }

        public double llGetTextureRot(int face)
        {
            LLObject.TextureEntry tex = m_host.Shape.Textures;
            if (face == -1)
            {
                face = 0;
            }
            return tex.GetFace((uint)face).Rotation;
        }

        public int llSubStringIndex(string source, string pattern)
        {
            return source.IndexOf(pattern);
        }

        public string llGetOwnerKey(string id)
        {
            NotImplemented("llGetOwnerKey");
            return "";
        }

        public LSL_Types.Vector3 llGetCenterOfMass()
        {
            NotImplemented("llGetCenterOfMass");
            return new LSL_Types.Vector3();
        }

        public LSL_Types.list llListSort(LSL_Types.list src, int stride, int ascending)
        {
            // SortedList<string, LSL_Types.list> sorted = new SortedList<string, LSL_Types.list>();
            // Add chunks to an array
            //int s = stride;
            //if (s < 1)
            //    s = 1;
            //int c = 0;
            //LSL_Types.list chunk = new LSL_Types.list();
            //string chunkString = "";
            //foreach (string element in src)
            //{
            //    c++;
            //    if (c > s)
            //    {
            //        sorted.Add(chunkString, chunk);
            //        chunkString = "";
            //        chunk = new LSL_Types.list();
            //        c = 0;
            //    }
            //    chunk.Add(element);
            //    chunkString += element.ToString();
            //}
            //if (chunk.Count > 0)
            //    sorted.Add(chunkString, chunk);

            //LSL_Types.list ret = new LSL_Types.list();
            //foreach (LSL_Types.list ls in sorted.Values)
            //{
            //    ret.AddRange(ls);
            //}

            //if (ascending == LSL_BaseClass.TRUE)
            //    return ret;
            //ret.Reverse();
            //return ret;
            NotImplemented("llListSort");
            return new LSL_Types.list();
        }

        public int llGetListLength(LSL_Types.list src)
        {
            return src.Length;
        }

        public int llList2Integer(LSL_Types.list src, int index)
        {
            if (index < 0)
            {
                index = src.Length + index;
            }
            if (index >= src.Length)
            {
                return 0;
            }
            return Convert.ToInt32(src.Data[index]);
        }

        public double osList2Double(LSL_Types.list src, int index)
        {
            if (index < 0)
            {
                index = src.Length + index;
            }
            if (index >= src.Length)
            {
                return 0.0;
            }
            return Convert.ToDouble(src.Data[index]);
        }

        public double llList2Float(LSL_Types.list src, int index)
        {
            if (index < 0)
            {
                index = src.Length + index;
            }
            if (index >= src.Length)
            {
                return 0.0;
            }
            return Convert.ToSingle(src.Data[index]);
        }

        public string llList2String(LSL_Types.list src, int index)
        {
            if (index < 0)
            {
                index = src.Length + index;
            }
            if (index >= src.Length)
            {
                return "";
            }
            return src.Data[index].ToString();
        }

        public string llList2Key(LSL_Types.list src, int index)
        {
            if (index < 0)
            {
                index = src.Length + index;
            }
            if (index >= src.Length)
            {
                return "00000000-0000-0000-0000-000000000000";
            }
            //return OpenSim.Framework.ToString(src[index]);
            LLUUID tmpkey;
            if (LLUUID.TryParse(src.Data[index].ToString(), out tmpkey))
            {
                return tmpkey.ToString();
            }
            else
            {
                return "00000000-0000-0000-0000-000000000000";
            }
        }

        public LSL_Types.Vector3 llList2Vector(LSL_Types.list src, int index)
        {
            if (index < 0)
            {
                index = src.Length + index;
            }
            if (index >= src.Length)
            {
                return new LSL_Types.Vector3(0, 0, 0);
            }
            if (src.Data[index].GetType() == typeof(OpenSim.Region.ScriptEngine.Common.LSL_Types.Vector3))
            {
                return (LSL_Types.Vector3)src.Data[index];
            }
            else
            {
                return new LSL_Types.Vector3(0, 0, 0);
            }
        }

        public LSL_Types.Quaternion llList2Rot(LSL_Types.list src, int index)
        {
            if (index < 0)
            {
                index = src.Length + index;
            }
            if (index >= src.Length)
            {
                return new LSL_Types.Quaternion(0, 0, 0, 1);
            }
            if (src.Data[index].GetType() == typeof(OpenSim.Region.ScriptEngine.Common.LSL_Types.Quaternion))
            {
                return (LSL_Types.Quaternion)src.Data[index];
            }
            else
            {
                return new LSL_Types.Quaternion(0, 0, 0, 1);
            }
        }

        public LSL_Types.list llList2List(LSL_Types.list src, int start, int end)
        {
            return src.GetSublist(start, end);
        }

        public LSL_Types.list llDeleteSubList(LSL_Types.list src, int start, int end)
        {
            //LSL_Types.list ret = new LSL_Types.list(src);
            //ret.RemoveRange(start, end - start);
            //return ret;

            // Just a hunch - needs testing
            return src.GetSublist(end, start);
        }

        public int llGetListEntryType(LSL_Types.list src, int index)
        {
            if (index < 0)
            {
                index = src.Length + index;
            }
            if (index >= src.Length)
            {
                return 0;
            }

            if (src.Data[index] is System.Int32)
                return 1;
            if (src.Data[index] is System.Double)
                return 2;
            if (src.Data[index] is System.String)
            {
                LLUUID tuuid;
                if (LLUUID.TryParse(src.Data[index].ToString(), out tuuid))
                {
                    return 3;
                }
                else
                {
                    return 4;
                }
            }
            if (src.Data[index] is OpenSim.Region.ScriptEngine.Common.LSL_Types.Vector3)
                return 5;
            if (src.Data[index] is OpenSim.Region.ScriptEngine.Common.LSL_Types.Quaternion)
                return 6;
            if (src.Data[index] is OpenSim.Region.ScriptEngine.Common.LSL_Types.list)
                return 7;
            return 0;

        }

        public string llList2CSV(LSL_Types.list src)
        {
            string ret = "";
            foreach (object o in src.Data)
            {
                ret = ret + o.ToString() + ",";
            }
            ret = ret.Substring(0, ret.Length - 2);
            return ret;
        }

        public LSL_Types.list llCSV2List(string src)
        {
            return new LSL_Types.list(src.Split(",".ToCharArray()));
        }

        public LSL_Types.list llListRandomize(LSL_Types.list src, int stride)
        {
            //int s = stride;
            //if (s < 1)
            //    s = 1;

            // This is a cowardly way of doing it ;)
            // TODO: Instead, randomize and check if random is mod stride or if it can not be, then array.removerange
            //List<LSL_Types.list> tmp = new List<LSL_Types.list>();

            // Add chunks to an array
            //int c = 0;
            //LSL_Types.list chunk = new LSL_Types.list();
            //foreach (string element in src)
            //{
            //    c++;
            //    if (c > s)
            //    {
            //        tmp.Add(chunk);
            //        chunk = new LSL_Types.list();
            //        c = 0;
            //    }
            //    chunk.Add(element);
            //}
            //if (chunk.Count > 0)
            //    tmp.Add(chunk);

            // Decreate (<- what kind of word is that? :D ) array back into a list
            //int rnd;
            //LSL_Types.list ret = new LSL_Types.list();
            //while (tmp.Count > 0)
            //{
            //    rnd = Util.RandomClass.Next(tmp.Count);
            //    foreach (string str in tmp[rnd])
            //    {
            //       ret.Add(str);
            //    }
            //    tmp.RemoveAt(rnd);
            //}

            //return ret;
            NotImplemented("llListRandomize");
            return new LSL_Types.list();
        }

        public LSL_Types.list llList2ListStrided(LSL_Types.list src, int start, int end, int stride)
        {
            LSL_Types.list ret = new LSL_Types.list();
            //int s = stride;
            //if (s < 1)
            //    s = 1;

            //int sc = s;
            //for (int i = start; i < src.Count; i++)
            //{
            //    sc--;
            //    if (sc == 0)
            //    {
            //        sc = s;
            //       // Addthis
            //        ret.Add(src[i]);
            //    }
            //    if (i == end)
            //        break;
            //}
            NotImplemented("llList2ListStrided");
            return ret;
        }

        public LSL_Types.Vector3 llGetRegionCorner()
        {
            return new LSL_Types.Vector3(World.RegionInfo.RegionLocX * 256, World.RegionInfo.RegionLocY * 256, 0);
        }

        public LSL_Types.list llListInsertList(LSL_Types.list dest, LSL_Types.list src, int start)
        {
            return dest.GetSublist(0, start - 1) + src + dest.GetSublist(start, -1);
        }

        public int llListFindList(LSL_Types.list src, LSL_Types.list test)
        {
            //foreach (string s in test)
            //{
            //    for (int ci = 0; ci < src.Count; ci++)
            //    {
            //        if (s == src[ci])
            //            return ci;
            //    }
            //}
            NotImplemented("llListFindList");
            return -1;
        }

        public string llGetObjectName()
        {
            return m_host.Name;
        }

        public void llSetObjectName(string name)
        {
            m_host.Name = name;
        }

        public string llGetDate()
        {
            DateTime date = DateTime.Now.ToUniversalTime();
            string result = date.ToString("yyyy-MM-dd");
            return result;
        }

        public int llEdgeOfWorld(LSL_Types.Vector3 pos, LSL_Types.Vector3 dir)
        {
            NotImplemented("llEdgeOfWorld");
            return 0;
        }

        public int llGetAgentInfo(string id)
        {
            NotImplemented("llGetAgentInfo");
            return 0;
        }

        public void llAdjustSoundVolume(double volume)
        {
            NotImplemented("llAdjustSoundVolume");
        }

        public void llSetSoundQueueing(int queue)
        {
            NotImplemented("llSetSoundQueueing");
        }

        public void llSetSoundRadius(double radius)
        {
            NotImplemented("llSetSoundRadius");
        }

        public string llKey2Name(string id)
        {
            NotImplemented("llKey2Name");
            return "";
        }

        public void llSetTextureAnim(int mode, int face, int sizex, int sizey, double start, double length, double rate)
        {
            NotImplemented("llSetTextureAnim");
        }

        public void llTriggerSoundLimited(string sound, double volume, LSL_Types.Vector3 top_north_east,
                                          LSL_Types.Vector3 bottom_south_west)
        {
            NotImplemented("llTriggerSoundLimited");
        }

        public void llEjectFromLand(string pest)
        {
            NotImplemented("llEjectFromLand");
        }

        public void llParseString2List()
        {
            NotImplemented("llParseString2List");
        }

        public int llOverMyLand(string id)
        {
            NotImplemented("llOverMyLand");
            return 0;
        }

        public string llGetLandOwnerAt(LSL_Types.Vector3 pos)
        {
            return World.GetLandOwner((float)pos.x, (float)pos.y).ToString();
        }

        public string llGetNotecardLine(string name, int line)
        {
            NotImplemented("llGetNotecardLine");
            return "";
        }

        public LSL_Types.Vector3 llGetAgentSize(string id)
        {
            NotImplemented("llGetAgentSize");
            return new LSL_Types.Vector3();
        }

        public int llSameGroup(string agent)
        {
            NotImplemented("llSameGroup");
            return 0;
        }

        public void llUnSit(string id)
        {
            NotImplemented("llUnSit");
        }

        public LSL_Types.Vector3 llGroundSlope(LSL_Types.Vector3 offset)
        {
            NotImplemented("llGroundSlope");
            return new LSL_Types.Vector3();
        }

        public LSL_Types.Vector3 llGroundNormal(LSL_Types.Vector3 offset)
        {
            NotImplemented("llGroundNormal");
            return new LSL_Types.Vector3();
        }

        public LSL_Types.Vector3 llGroundContour(LSL_Types.Vector3 offset)
        {
            NotImplemented("llGroundContour");
            return new LSL_Types.Vector3();
        }

        public int llGetAttached()
        {
            NotImplemented("llGetAttached");
            return 0;
        }

        public int llGetFreeMemory()
        {
            NotImplemented("llGetFreeMemory");
            return 0;
        }

        public string llGetRegionName()
        {
            return World.RegionInfo.RegionName;
        }

        public double llGetRegionTimeDilation()
        {
            return (double)World.TimeDilation;
        }

        public double llGetRegionFPS()
        {
            return 10.0f;
        }

        /* particle system rules should be coming into this routine as doubles, that is
        rule[0] should be an integer from this list and rule[1] should be the arg
        for the same integer. wiki.secondlife.com has most of this mapping, but some
        came from http://www.caligari-designs.com/p4u2

        We iterate through the list for 'Count' elements, incrementing by two for each
        iteration and set the members of Primitive.ParticleSystem, one at a time.
        */

        public enum PrimitiveRule : int
        {
            PSYS_PART_FLAGS = 0,
            PSYS_PART_START_COLOR = 1,
            PSYS_PART_START_ALPHA = 2,
            PSYS_PART_END_COLOR = 3,
            PSYS_PART_END_ALPHA = 4,
            PSYS_PART_START_SCALE = 5,
            PSYS_PART_END_SCALE = 6,
            PSYS_PART_MAX_AGE = 7,
            PSYS_SRC_ACCEL = 8,
            PSYS_SRC_PATTERN = 9,
            PSYS_SRC_TEXTURE = 12,
            PSYS_SRC_BURST_RATE = 13,
            PSYS_SRC_BURST_PART_COUNT = 15,
            PSYS_SRC_BURST_RADIUS = 16,
            PSYS_SRC_BURST_SPEED_MIN = 17,
            PSYS_SRC_BURST_SPEED_MAX = 18,
            PSYS_SRC_MAX_AGE = 19,
            PSYS_SRC_TARGET_KEY = 20,
            PSYS_SRC_OMEGA = 21,
            PSYS_SRC_ANGLE_BEGIN = 22,
            PSYS_SRC_ANGLE_END = 23
        }

        public void llParticleSystem(List<Object> rules)
        {
            Primitive.ParticleSystem prules = new Primitive.ParticleSystem();
            for (int i = 0; i < rules.Count; i += 2)
            {
                switch ((int)rules[i])
                {
                    case (int)LSL_BaseClass.PSYS_PART_FLAGS:
                        prules.PartFlags = (uint)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_PART_START_COLOR:
                        prules.PartStartColor = (LLColor)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_PART_START_ALPHA:
                        //what is the cast?                    prules.PartStartColor = (LSL_Types.Vec)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_PART_END_COLOR:
                        prules.PartEndColor = (LLColor)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_PART_END_ALPHA:
                        //what is the cast?                    prules.PartStartColor = (LLColor)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_PART_START_SCALE:
                        //what is the cast?                    prules.PartStartColor = (LLColor)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_PART_END_SCALE:
                        //what is the cast?                    prules.PartStartColor = (LLColor)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_PART_MAX_AGE:
                        prules.MaxAge = (float)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_SRC_ACCEL:
                        //what is the cast?                    prules.PartStartColor = (LLColor)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_SRC_PATTERN:
                        //what is the cast?                    prules.PartStartColor = (LLColor)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_SRC_TEXTURE:
                        prules.Texture = (LLUUID)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_SRC_BURST_RATE:
                        prules.BurstRate = (float)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_SRC_BURST_PART_COUNT:
                        prules.BurstPartCount = (byte)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_SRC_BURST_RADIUS:
                        prules.BurstRadius = (float)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_SRC_BURST_SPEED_MIN:
                        prules.BurstSpeedMin = (float)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_SRC_BURST_SPEED_MAX:
                        prules.BurstSpeedMax = (float)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_SRC_MAX_AGE:
                        prules.MaxAge = (float)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_SRC_TARGET_KEY:
                        prules.Target = (LLUUID)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_SRC_OMEGA:
                        //cast??                    prules.MaxAge = (float)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_SRC_ANGLE_BEGIN:
                        prules.InnerAngle = (float)rules[i + 1];
                        break;

                    case (int)LSL_BaseClass.PSYS_SRC_ANGLE_END:
                        prules.OuterAngle = (float)rules[i + 1];
                        break;
                }
            }

            m_host.AddNewParticleSystem(prules);
        }

        public void llGroundRepel(double height, int water, double tau)
        {
            NotImplemented("llGroundRepel");
        }

        public void llGiveInventoryList()
        {
            NotImplemented("llGiveInventoryList");
        }

        public void llSetVehicleType(int type)
        {
            NotImplemented("llSetVehicleType");
        }

        public void llSetVehicledoubleParam(int param, double value)
        {
            NotImplemented("llSetVehicledoubleParam");
        }

        public void llSetVehicleVectorParam(int param, LSL_Types.Vector3 vec)
        {
            NotImplemented("llSetVehicleVectorParam");
        }

        public void llSetVehicleRotationParam(int param, LSL_Types.Quaternion rot)
        {
            NotImplemented("llSetVehicleRotationParam");
        }

        public void llSetVehicleFlags(int flags)
        {
            NotImplemented("llSetVehicleFlags");
        }

        public void llRemoveVehicleFlags(int flags)
        {
            NotImplemented("llRemoveVehicleFlags");
        }

        public void llSitTarget(LSL_Types.Vector3 offset, LSL_Types.Quaternion rot)
        {
            // LSL quaternions can normalize to 0, normal Quaternions can't.
            if (rot.s == 0 && rot.x == 0 && rot.y == 0 && rot.z == 0)
                rot.z = 1; // ZERO_ROTATION = 0,0,0,1

            m_host.SetSitTarget(new Vector3((float)offset.x, (float)offset.y, (float)offset.z), new Quaternion((float)rot.s, (float)rot.x, (float)rot.y, (float)rot.z));
        }

        public string llAvatarOnSitTarget()
        {
            LLUUID AVID = m_host.GetAvatarOnSitTarget();

            if (AVID != LLUUID.Zero)
                return AVID.ToString();
            else
                return "";
        }

        public void llAddToLandPassList(string avatar, double hours)
        {
            NotImplemented("llAddToLandPassList");
        }

        public void llSetTouchText(string text)
        {
            m_host.TouchName = text;
        }

        public void llSetSitText(string text)
        {
            m_host.SitName = text;
        }

        public void llSetCameraEyeOffset(LSL_Types.Vector3 offset)
        {
            NotImplemented("llSetCameraEyeOffset");
        }

        public void llSetCameraAtOffset(LSL_Types.Vector3 offset)
        {
            NotImplemented("llSetCameraAtOffset");
        }

        public string llDumpList2String(LSL_Types.list src, string seperator)
        {
            if (src.Length == 0)
            {
                return "";
            }
            string ret = "";
            foreach (object o in src.Data)
            {
                ret = ret + o.ToString() + seperator;
            }
            ret = ret.Substring(0, ret.Length - seperator.Length);
            return ret;
        }

        public void llScriptDanger(LSL_Types.Vector3 pos)
        {
            NotImplemented("llScriptDanger");
        }

        public void llDialog(string avatar, string message, LSL_Types.list buttons, int chat_channel)
        {
            NotImplemented("llDialog");
        }

        public void llVolumeDetect(int detect)
        {
            NotImplemented("llVolumeDetect");
        }

        public void llResetOtherScript(string name)
        {
            NotImplemented("llResetOtherScript");
        }

        public int llGetScriptState(string name)
        {
            NotImplemented("llGetScriptState");
            return 0;
        }

        public void llRemoteLoadScript()
        {
            NotImplemented("llRemoteLoadScript");
        }

        public void llSetRemoteScriptAccessPin(int pin)
        {
            NotImplemented("llSetRemoteScriptAccessPin");
        }

        public void llRemoteLoadScriptPin(string target, string name, int pin, int running, int start_param)
        {
            NotImplemented("llRemoteLoadScriptPin");
        }

        //  remote_data(integer type, key channel, key message_id, string sender, integer ival, string sval)
        // Not sure where these constants should live:
        // REMOTE_DATA_CHANNEL = 1
        // REMOTE_DATA_REQUEST = 2
        // REMOTE_DATA_REPLY = 3
        public void llOpenRemoteDataChannel()
        {
            IXMLRPC xmlrpcMod = m_ScriptEngine.World.RequestModuleInterface<IXMLRPC>();
            if (xmlrpcMod.IsEnabled())
            {
                LLUUID channelID = xmlrpcMod.OpenXMLRPCChannel(m_localID, m_itemID);
                object[] resobj = new object[] { 1, channelID.ToString(), LLUUID.Zero.ToString(), "", 0, "" };
                m_ScriptEngine.m_EventQueueManager.AddToScriptQueue(m_localID, m_itemID, "remote_data", resobj);
            }
        }

        public string llSendRemoteData(string channel, string dest, int idata, string sdata)
        {
            NotImplemented("llSendRemoteData");
            return "";
        }

        public void llRemoteDataReply(string channel, string message_id, string sdata, int idata)
        {
            IXMLRPC xmlrpcMod = m_ScriptEngine.World.RequestModuleInterface<IXMLRPC>();
            xmlrpcMod.RemoteDataReply(channel, message_id, sdata, idata);
        }

        public void llCloseRemoteDataChannel(string channel)
        {
            IXMLRPC xmlrpcMod = m_ScriptEngine.World.RequestModuleInterface<IXMLRPC>();
            xmlrpcMod.CloseXMLRPCChannel(channel);
        }

        public string llMD5String(string src, int nonce)
        {
            return Util.Md5Hash(src + ":" + nonce.ToString());
        }

        public void llSetPrimitiveParams(LSL_Types.list rules)
        {
            NotImplemented("llSetPrimitiveParams");
        }

        public string llStringToBase64(string str)
        {
            try
            {
                byte[] encData_byte = new byte[str.Length];
                encData_byte = Encoding.UTF8.GetBytes(str);
                string encodedData = Convert.ToBase64String(encData_byte);
                return encodedData;
            }
            catch (Exception e)
            {
                throw new Exception("Error in base64Encode" + e.Message);
            }
        }

        public string llBase64ToString(string str)
        {
            UTF8Encoding encoder = new UTF8Encoding();
            Decoder utf8Decode = encoder.GetDecoder();
            try
            {
                byte[] todecode_byte = Convert.FromBase64String(str);
                int charCount = utf8Decode.GetCharCount(todecode_byte, 0, todecode_byte.Length);
                char[] decoded_char = new char[charCount];
                utf8Decode.GetChars(todecode_byte, 0, todecode_byte.Length, decoded_char, 0);
                string result = new String(decoded_char);
                return result;
            }
            catch (Exception e)
            {
                throw new Exception("Error in base64Decode" + e.Message);
            }
        }

        public void llXorBase64Strings()
        {
            throw new Exception("Command deprecated! Use llXorBase64StringsCorrect instead.");
        }

        public void llRemoteDataSetRegion()
        {
            NotImplemented("llRemoteDataSetRegion");
        }

        public double llLog10(double val)
        {
            return (double)Math.Log10(val);
        }

        public double llLog(double val)
        {
            return (double)Math.Log(val);
        }

        public LSL_Types.list llGetAnimationList(string id)
        {
            NotImplemented("llGetAnimationList");
            return new LSL_Types.list();
        }

        public void llSetParcelMusicURL(string url)
        {
            LLUUID landowner = World.GetLandOwner(m_host.AbsolutePosition.X, m_host.AbsolutePosition.Y);
            if (landowner.Equals(null))
            {
                return;
            }
            if (landowner != m_host.ObjectOwner)
            {
                return;
            }
            World.SetLandMusicURL(m_host.AbsolutePosition.X, m_host.AbsolutePosition.Y, url);
        }

        public LSL_Types.Vector3 llGetRootPosition()
        {
            return new LSL_Types.Vector3(m_host.ParentGroup.AbsolutePosition.X, m_host.ParentGroup.AbsolutePosition.Y, m_host.ParentGroup.AbsolutePosition.Z);
        }

        public LSL_Types.Quaternion llGetRootRotation()
        {
            return new LSL_Types.Quaternion(m_host.ParentGroup.GroupRotation.X, m_host.ParentGroup.GroupRotation.Y, m_host.ParentGroup.GroupRotation.Z, m_host.ParentGroup.GroupRotation.W);
        }

        public string llGetObjectDesc()
        {
            return m_host.Description;
        }

        public void llSetObjectDesc(string desc)
        {
            m_host.Description = desc;
        }

        public string llGetCreator()
        {
            return m_host.ObjectCreator.ToString();
        }

        public string llGetTimestamp()
        {
            return DateTime.Now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
        }

        public void llSetLinkAlpha(int linknumber, double alpha, int face)
        {
            SceneObjectPart part = m_host.ParentGroup.GetLinkNumPart(linknumber);
            if (linknumber > -1)
            {
                LLObject.TextureEntry tex = part.Shape.Textures;
                LLColor texcolor;
                if (face > -1)
                {
                    texcolor = tex.CreateFace((uint)face).RGBA;
                    texcolor.A = (float)Math.Abs(alpha - 1);
                    tex.FaceTextures[face].RGBA = texcolor;
                    part.UpdateTexture(tex);
                    return;
                }
                else if (face == -1)
                {
                    texcolor = tex.DefaultTexture.RGBA;
                    texcolor.A = (float)Math.Abs(alpha - 1);
                    tex.DefaultTexture.RGBA = texcolor;
                    for (uint i = 0; i < 32; i++)
                    {
                        if (tex.FaceTextures[i] != null)
                        {
                            texcolor = tex.FaceTextures[i].RGBA;
                            texcolor.A = (float)Math.Abs(alpha - 1);
                            tex.FaceTextures[i].RGBA = texcolor;
                        }
                    }
                    texcolor = tex.DefaultTexture.RGBA;
                    texcolor.A = (float)Math.Abs(alpha - 1);
                    tex.DefaultTexture.RGBA = texcolor;
                    part.UpdateTexture(tex);
                    return;
                }
                return;
            }
            else if (linknumber == -1)
            {
                int num = m_host.ParentGroup.PrimCount;
                for (int w = 0; w < num; w++)
                {
                    linknumber = w;
                    part = m_host.ParentGroup.GetLinkNumPart(linknumber);
                    LLObject.TextureEntry tex = part.Shape.Textures;
                    LLColor texcolor;
                    if (face > -1)
                    {
                        texcolor = tex.CreateFace((uint)face).RGBA;
                        texcolor.A = (float)Math.Abs(alpha - 1);
                        tex.FaceTextures[face].RGBA = texcolor;
                        part.UpdateTexture(tex);
                    }
                    else if (face == -1)
                    {
                        texcolor = tex.DefaultTexture.RGBA;
                        texcolor.A = (float)Math.Abs(alpha - 1);
                        tex.DefaultTexture.RGBA = texcolor;
                        for (uint i = 0; i < 32; i++)
                        {
                            if (tex.FaceTextures[i] != null)
                            {
                                texcolor = tex.FaceTextures[i].RGBA;
                                texcolor.A = (float)Math.Abs(alpha - 1);
                                tex.FaceTextures[i].RGBA = texcolor;
                            }
                        }
                        texcolor = tex.DefaultTexture.RGBA;
                        texcolor.A = (float)Math.Abs(alpha - 1);
                        tex.DefaultTexture.RGBA = texcolor;
                        part.UpdateTexture(tex);
                    }
                }
                return;
            }
            else
            {
                NotImplemented("llSetLinkAlpha");
            }
        }

        public int llGetNumberOfPrims()
        {
            return m_host.ParentGroup.PrimCount;
        }

        public string llGetNumberOfNotecardLines(string name)
        {
            NotImplemented("llGetNumberOfNotecardLines");
            return "";
        }

        public LSL_Types.list llGetBoundingBox(string obj)
        {
            NotImplemented("llGetBoundingBox");
            return new LSL_Types.list();
        }

        public LSL_Types.Vector3 llGetGeometricCenter()
        {
            return new LSL_Types.Vector3(m_host.GetGeometricCenter().X, m_host.GetGeometricCenter().Y, m_host.GetGeometricCenter().Z);
        }

        public void llGetPrimitiveParams()
        {
            NotImplemented("llGetPrimitiveParams");
        }

        public string llIntegerToBase64(int number)
        {
            NotImplemented("llIntegerToBase64");
            return "";
        }

        public int llBase64ToInteger(string str)
        {
            NotImplemented("llBase64ToInteger");
            return 0;
        }

        public double llGetGMTclock()
        {
            return DateTime.UtcNow.TimeOfDay.TotalSeconds;
        }

        public string llGetSimulatorHostname()
        {
            return System.Environment.MachineName;
        }

        public void llSetLocalRot(LSL_Types.Quaternion rot)
        {
            m_host.RotationOffset = new LLQuaternion((float)rot.x, (float)rot.y, (float)rot.z, (float)rot.s);
        }

        public LSL_Types.list llParseStringKeepNulls(string src, LSL_Types.list seperators, LSL_Types.list spacers)
        {
            NotImplemented("llParseStringKeepNulls");
            return new LSL_Types.list();
        }

        public void llRezAtRoot(string inventory, LSL_Types.Vector3 position, LSL_Types.Vector3 velocity,
                                LSL_Types.Quaternion rot, int param)
        {
            NotImplemented("llRezAtRoot");
        }

        public int llGetObjectPermMask(int mask)
        {
            NotImplemented("llGetObjectPermMask");
            return 0;
        }

        public void llSetObjectPermMask(int mask, int value)
        {
            NotImplemented("llSetObjectPermMask");
        }

        public void llGetInventoryPermMask(string item, int mask)
        {
            NotImplemented("llGetInventoryPermMask");
        }

        public void llSetInventoryPermMask(string item, int mask, int value)
        {
            NotImplemented("llSetInventoryPermMask");
        }

        public string llGetInventoryCreator(string item)
        {
            NotImplemented("llGetInventoryCreator");
            return "";
        }

        public void llOwnerSay(string msg)
        {
            //temp fix so that lsl wiki examples aren't annoying to use to test other functions
            World.SimChat(Helpers.StringToField(msg), ChatTypeEnum.Say, 0, m_host.AbsolutePosition, m_host.Name, m_host.UUID);
            IWorldComm wComm = m_ScriptEngine.World.RequestModuleInterface<IWorldComm>();
            wComm.DeliverMessage(m_host.UUID.ToString(), ChatTypeEnum.Say, 0, m_host.Name, msg);
        }

        public void llRequestSimulatorData(string simulator, int data)
        {
            NotImplemented("llRequestSimulatorData");
        }

        public void llForceMouselook(int mouselook)
        {
            NotImplemented("llForceMouselook");
        }

        public double llGetObjectMass(string id)
        {
            NotImplemented("llGetObjectMass");
            return 0;
        }

        public LSL_Types.list llListReplaceList(LSL_Types.list dest, LSL_Types.list src, int start, int end)
        {
            return dest.GetSublist(0, start - 1) + src + dest.GetSublist(end + 1, -1);
        }

        public void llLoadURL(string avatar_id, string message, string url)
        {
            LLUUID avatarId = new LLUUID(avatar_id);
            m_ScriptEngine.World.SendUrlToUser(avatarId, m_host.Name, m_host.UUID, m_host.ObjectOwner, false, message,
                                               url);
        }

        public void llParcelMediaCommandList(LSL_Types.list commandList)
        {
            NotImplemented("llParcelMediaCommandList");
        }

        public void llParcelMediaQuery()
        {
            NotImplemented("llParcelMediaQuery");
        }

        public int llModPow(int a, int b, int c)
        {
            Int64 tmp = 0;
            Int64 val = Math.DivRem(Convert.ToInt64(Math.Pow(a, b)), c, out tmp);
            return Convert.ToInt32(tmp);
        }

        public int llGetInventoryType(string name)
        {
            NotImplemented("llGetInventoryType");
            return 0;
        }

        public void llSetPayPrice(int price, LSL_Types.list quick_pay_buttons)
        {
            NotImplemented("llSetPayPrice");
        }

        public LSL_Types.Vector3 llGetCameraPos()
        {
            NotImplemented("llGetCameraPos");
            return new LSL_Types.Vector3();
        }

        public LSL_Types.Quaternion llGetCameraRot()
        {
            NotImplemented("llGetCameraRot");
            return new LSL_Types.Quaternion();
        }

        public void llSetPrimURL()
        {
            NotImplemented("llSetPrimURL");
        }

        public void llRefreshPrimURL()
        {
            NotImplemented("llRefreshPrimURL");
        }

        public string llEscapeURL(string url)
        {
            try
            {
                return Uri.EscapeUriString(url);
            }
            catch (Exception ex)
            {
                return "llEscapeURL: " + ex.ToString();
            }
        }

        public string llUnescapeURL(string url)
        {
            try
            {
                return Uri.UnescapeDataString(url);
            }
            catch (Exception ex)
            {
                return "llUnescapeURL: " + ex.ToString();
            }
        }

        public void llMapDestination(string simname, LSL_Types.Vector3 pos, LSL_Types.Vector3 look_at)
        {
            NotImplemented("llMapDestination");
        }

        public void llAddToLandBanList(string avatar, double hours)
        {
            NotImplemented("llAddToLandBanList");
        }

        public void llRemoveFromLandPassList(string avatar)
        {
            NotImplemented("llRemoveFromLandPassList");
        }

        public void llRemoveFromLandBanList(string avatar)
        {
            NotImplemented("llRemoveFromLandBanList");
        }

        public void llSetCameraParams(LSL_Types.list rules)
        {
            NotImplemented("llSetCameraParams");
        }

        public void llClearCameraParams()
        {
            NotImplemented("llClearCameraParams");
        }

        public double llListStatistics(int operation, LSL_Types.list src)
        {
            NotImplemented("llListStatistics");
            return 0;
        }

        public int llGetUnixTime()
        {
            return Util.UnixTimeSinceEpoch();
        }

        public int llGetParcelFlags(LSL_Types.Vector3 pos)
        {
            NotImplemented("llGetParcelFlags");
            return 0;
        }

        public int llGetRegionFlags()
        {
            NotImplemented("llGetRegionFlags");
            return 0;
        }

        public string llXorBase64StringsCorrect(string str1, string str2)
        {
            string ret = "";
            string src1 = llBase64ToString(str1);
            string src2 = llBase64ToString(str2);
            int c = 0;
            for (int i = 0; i < src1.Length; i++)
            {
                ret += src1[i] ^ src2[c];

                c++;
                if (c > src2.Length)
                    c = 0;
            }
            return llStringToBase64(ret);
        }

        public string llHTTPRequest(string url, LSL_Types.list parameters, string body)
        {
            IHttpRequests httpScriptMod =
                m_ScriptEngine.World.RequestModuleInterface<IHttpRequests>();
            List<string> param = new List<string>();
            foreach (object o in parameters.Data)
            {
                param.Add(o.ToString());
            }
            LLUUID reqID = httpScriptMod.
                    StartHttpRequest(m_localID, m_itemID, url, param, body);

            if (!reqID.Equals(null))
                return reqID.ToString();
            else
                return null;
        }

        public void llResetLandBanList()
        {
            NotImplemented("llResetLandBanList");
        }

        public void llResetLandPassList()
        {
            NotImplemented("llResetLandPassList");
        }

        public int llGetParcelPrimCount(LSL_Types.Vector3 pos, int category, int sim_wide)
        {
            NotImplemented("llGetParcelPrimCount");
            return 0;
        }

        public LSL_Types.list llGetParcelPrimOwners(LSL_Types.Vector3 pos)
        {
            NotImplemented("llGetParcelPrimOwners");
            return new LSL_Types.list();
        }

        public int llGetObjectPrimCount(string object_id)
        {
            SceneObjectPart part = World.GetSceneObjectPart(new LLUUID(object_id));
            if (part == null)
            {
                return 0;
            }
            else
            {
                return part.ParentGroup.Children.Count;
            }
        }

        public int llGetParcelMaxPrims(LSL_Types.Vector3 pos, int sim_wide)
        {
            // Alondria: This currently just is utilizing the normal grid's 0.22 prims/m2 calculation
            // Which probably will be irrelevent in OpenSim....
            LandData land = World.GetLandData((float)pos.x, (float)pos.y);
            float bonusfactor = World.RegionInfo.EstateSettings.objectBonusFactor;
            if (land == null)
            {
                return 0;
            }
            if (sim_wide == 1)
            {
                decimal v = land.simwideArea * (decimal)(0.22) * (decimal)bonusfactor;
                return (int)v;
            }
            else
            {
                decimal v = land.area * (decimal)(0.22) * (decimal)bonusfactor;
                return (int)v;
            }

        }

        public LSL_Types.list llGetParcelDetails(LSL_Types.Vector3 pos, LSL_Types.list param)
        {
            LandData land = World.GetLandData((float)pos.x, (float)pos.y);
            if (land == null)
            {
                return new LSL_Types.list(0);
            }
            LSL_Types.list ret = new LSL_Types.list();
            foreach (object o in param.Data)
            {
                switch (o.ToString())
                {
                    case "0":
                        ret = ret + new LSL_Types.list(land.landName);
                        break;
                    case "1":
                        ret = ret + new LSL_Types.list(land.landDesc);
                        break;
                    case "2":
                        ret = ret + new LSL_Types.list(land.ownerID.ToString());
                        break;
                    case "3":
                        ret = ret + new LSL_Types.list(land.groupID.ToString());
                        break;
                    case "4":
                        ret = ret + new LSL_Types.list(land.area);
                        break;
                    default:
                        ret = ret + new LSL_Types.list(0);
                        break;
                }
            }
            return ret;
        }

        //
        // OpenSim functions
        //
        public int osTerrainSetHeight(int x, int y, double val)
        {
            if (x > 255 || x < 0 || y > 255 || y < 0)
                LSLError("osTerrainSetHeight: Coordinate out of bounds");

            if (World.PermissionsMngr.CanTerraform(m_host.OwnerID, new LLVector3(x, y, 0)))
            {
                World.Terrain.Set(x, y, val);
                return 1;
            }
            else
            {
                return 0;
            }
        }

        public double osTerrainGetHeight(int x, int y)
        {
            if (x > 255 || x < 0 || y > 255 || y < 0)
                LSLError("osTerrainGetHeight: Coordinate out of bounds");

            return World.Terrain.GetHeight(x, y);
        }

        public int osRegionRestart(double seconds)
        {
            if (World.PermissionsMngr.CanRestartSim(m_host.OwnerID))
            {
                World.Restart((float)seconds);
                return 1;
            }
            else
            {
                return 0;
            }
        }

        public void osRegionNotice(string msg)
        {
            World.SendGeneralAlert(msg);
        }

        public string osSetDynamicTextureURL(string dynamicID, string contentType, string url, string extraParams,
                                             int timer)
        {
            if (dynamicID == "")
            {
                IDynamicTextureManager textureManager = World.RequestModuleInterface<IDynamicTextureManager>();
                LLUUID createdTexture =
                    textureManager.AddDynamicTextureURL(World.RegionInfo.RegionID, m_host.UUID, contentType, url,
                                                        extraParams, timer);
                return createdTexture.ToString();
            }
            else
            {
                //TODO update existing dynamic textures
            }

            return LLUUID.Zero.ToString();
        }

        private void NotImplemented(string Command)
        {
            if (throwErrorOnNotImplemented)
                throw new NotImplementedException("Command not implemented: " + Command);
        }

        private void LSLError(string msg)
        {
            throw new Exception("LSL Runtime Error: " + msg);
        }
    }
}
