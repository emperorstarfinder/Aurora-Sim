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
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using log4net;
using OpenSim.Region.DataSnapshot.Interfaces;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Environment.Modules.World.Land;
using OpenSim.Framework;
using OpenMetaverse;

namespace OpenSim.Region.DataSnapshot.Providers
{
    public class ObjectSnapshot : IDataSnapshotProvider
    {
        private Scene m_scene = null;
        // private DataSnapshotManager m_parent = null;
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private bool m_stale = true;

        public void Initialize(Scene scene, DataSnapshotManager parent)
        {
            m_scene = scene;
            // m_parent = parent;

            //To check for staleness, we must catch all incoming client packets.
            m_scene.EventManager.OnNewClient += OnNewClient;
            m_scene.EventManager.OnParcelPrimCountAdd += delegate(SceneObjectGroup obj) { this.Stale = true; };
        }

        public void OnNewClient(IClientAPI client)
        {
            //Detect object data changes by hooking into the IClientAPI.
            //Very dirty, and breaks whenever someone changes the client API.

            client.OnAddPrim += delegate (UUID ownerID, UUID groupID, Vector3 RayEnd, Quaternion rot,
                PrimitiveBaseShape shape, byte bypassRaycast, Vector3 RayStart, UUID RayTargetID,
                byte RayEndIsIntersection) { this.Stale = true; };
            client.OnLinkObjects += delegate (IClientAPI remoteClient, uint parent, List<uint> children)
                { this.Stale = true; };
            client.OnDelinkObjects += delegate(List<uint> primIds) { this.Stale = true; };
            client.OnGrabUpdate += delegate(UUID objectID, Vector3 offset, Vector3 grapPos,
                IClientAPI remoteClient, List<SurfaceTouchEventArgs> surfaceArgs) { this.Stale = true; };
            client.OnObjectAttach += delegate(IClientAPI remoteClient, uint objectLocalID, uint AttachmentPt,
                Quaternion rot, bool silent) { this.Stale = true; };
            client.OnObjectDuplicate += delegate(uint localID, Vector3 offset, uint dupeFlags, UUID AgentID,
                UUID GroupID) { this.Stale = true; };
            client.OnObjectDuplicateOnRay += delegate(uint localID, uint dupeFlags, UUID AgentID, UUID GroupID,
                UUID RayTargetObj, Vector3 RayEnd, Vector3 RayStart, bool BypassRaycast,
                bool RayEndIsIntersection, bool CopyCenters, bool CopyRotates) { this.Stale = true; };
            client.OnObjectIncludeInSearch += delegate(IClientAPI remoteClient, bool IncludeInSearch, uint localID)
                { this.Stale = true; };
            client.OnObjectPermissions += delegate(IClientAPI controller, UUID agentID, UUID sessionID,
                byte field, uint localId, uint mask, byte set) { this.Stale = true; };
            client.OnRezObject += delegate(IClientAPI remoteClient, UUID itemID, Vector3 RayEnd,
                Vector3 RayStart, UUID RayTargetID, byte BypassRayCast, bool RayEndIsIntersection,
                bool RezSelected,
                bool RemoveItem, UUID fromTaskID) { this.Stale = true; };
        }

        public Scene GetParentScene
        {
            get { return m_scene; }
        }

        public XmlNode RequestSnapshotData(XmlDocument nodeFactory)
        {
            m_log.Debug("[DATASNAPSHOT]: Generating object data for scene " + m_scene.RegionInfo.RegionName);

            XmlNode parent = nodeFactory.CreateNode(XmlNodeType.Element, "objectdata", "");
            XmlNode node;

            foreach (EntityBase entity in m_scene.Entities)
            {
                // only objects, not avatars
                if (entity is SceneObjectGroup)
                {
                    SceneObjectGroup obj = (SceneObjectGroup)entity;

//                    m_log.Debug("[DATASNAPSHOT]: Found object " + obj.Name + " in scene");

                    // libomv will complain about PrimFlags.JointWheel
                    // being obsolete, so we...
                    #pragma warning disable 0612
                    if ((obj.RootPart.Flags & PrimFlags.JointWheel) == PrimFlags.JointWheel)
                    {
                        SceneObjectPart m_rootPart = obj.RootPart;

                        if (m_rootPart != null)
                        {
                            ILandObject land = m_scene.LandChannel.GetLandObject(m_rootPart.AbsolutePosition.X, m_rootPart.AbsolutePosition.Y);

                            XmlNode xmlobject = nodeFactory.CreateNode(XmlNodeType.Element, "object", "");
                            node = nodeFactory.CreateNode(XmlNodeType.Element, "uuid", "");
                            node.InnerText = obj.UUID.ToString();
                            xmlobject.AppendChild(node);

                            node = nodeFactory.CreateNode(XmlNodeType.Element, "title", "");
                            node.InnerText = m_rootPart.Name;
                            xmlobject.AppendChild(node);

                            node = nodeFactory.CreateNode(XmlNodeType.Element, "description", "");
                            node.InnerText = m_rootPart.Description;
                            xmlobject.AppendChild(node);

                            node = nodeFactory.CreateNode(XmlNodeType.Element, "flags", "");
                            node.InnerText = String.Format("{0:x}", m_rootPart.ObjectFlags);
                            xmlobject.AppendChild(node);

                            node = nodeFactory.CreateNode(XmlNodeType.Element, "regionuuid", "");
                            node.InnerText = m_scene.RegionInfo.RegionSettings.RegionUUID.ToString();
                            xmlobject.AppendChild(node);

                            node = nodeFactory.CreateNode(XmlNodeType.Element, "parceluuid", "");
                            node.InnerText = land.landData.GlobalID.ToString();
                            xmlobject.AppendChild(node);

                            parent.AppendChild(xmlobject);
                        }
                    }
                    #pragma warning disable 0612
                }
            }
            this.Stale = false;
            return parent;
        }

        public String Name
        {
            get { return "ObjectSnapshot"; }
        }

        public bool Stale
        {
            get
            {
                return m_stale;
            }
            set
            {
                m_stale = value;

                if (m_stale)
                    OnStale(this);
            }
        }

        public event ProviderStale OnStale;
    }
}
