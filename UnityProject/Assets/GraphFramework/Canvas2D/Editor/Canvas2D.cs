using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditorInternal; 
using Object = UnityEngine.Object;

//#pragma warning disable 0414
//#pragma warning disable 0219 

namespace UnityEditor
{
	namespace Experimental
	{
		public interface ICanvasDataSource
		{
			CanvasElement[] FetchElements();
			void DeleteElement(CanvasElement e);
		}

		public enum UpdateType
		{
			eCandidate = 0,
			eUpdate = 1
		};

		public delegate bool CanvasEvent(CanvasElement element, Event e, Canvas2D parent);

	    public delegate void ModalWindowProc(Canvas2D parent);

		public class CanvasElement : IBounds
		{
			[Flags]
			public enum Capabilities
			{
				Normal,
				Unselectable,
				DoesNotCollapse
			}

			protected int m_zIndex = 0;
			protected List<CanvasElement> m_Children = new List<CanvasElement>();
			protected List<CanvasElement> m_Dependencies = new List<CanvasElement>();
			protected Vector3 m_Translation = Vector3.zero;
			protected Vector3 m_Scale = Vector3.one;
			protected bool m_Selected = false;
			protected bool m_Collapsed = false;
			protected Texture2D m_Texture = null;
			protected CanvasElement m_Parent = null;
			protected Object m_Target = null;
			protected bool m_SupportsRenderToTexture = true;
			private bool m_Dirty = true;
			protected Capabilities m_Caps = Capabilities.Normal;

			public event CanvasEvent MouseDown;
			public event CanvasEvent MouseDrag;
			public event CanvasEvent MouseUp;
			public event CanvasEvent DoubleClick;
			public event CanvasEvent ScrollWheel;
			public event CanvasEvent KeyDown;
			public event CanvasEvent OnWidget;
			public event CanvasEvent ContextClick;
			public event CanvasEvent DragPerform;
			public event CanvasEvent DragExited;
			public event CanvasEvent DragUpdated;
			public event CanvasEvent AllEvents;

			public Capabilities Caps
			{
				get { return m_Caps; }
				set { m_Caps = value; }
			}

			public CanvasElement Parent
			{
				get { return m_Parent; }
				set { m_Parent = value; }
			}

			public Object Target
			{
				get { return m_Target; }
				set { m_Target = value; }
			}

			public bool selected
			{
				get { return m_Selected; }
				set
				{
					if ((Caps & Capabilities.Unselectable) == Capabilities.Unselectable)
						value = false;
					m_Selected = value;
					foreach (CanvasElement e in m_Children)
					{
						e.selected = value;
					}
					Invalidate();
				}
			}

			public bool collapsed
			{
				get { return m_Collapsed; }
				set
				{
					m_Collapsed = value;
					foreach (CanvasElement e in m_Children)
					{
						e.collapsed = value;
					}
					UpdateModel(UpdateType.eUpdate);
				}
			}

			public T FindParent<T>() where T : class
			{
				if (m_Parent == null)
					return default(T);

				T casted = m_Parent as T;

				if (casted != null)
				{
					return casted;
				}

				return m_Parent.FindParent<T>();
			}

			public Canvas2D ParentCanvas()
			{
				CanvasElement e = FindTopMostParent();
				if (e is Canvas2D)
					return e as Canvas2D;

				return e.Parent as Canvas2D;
			}

			public CanvasElement FindTopMostParent()
			{
				if (m_Parent == null)
					return null;

				if (m_Parent is Canvas2D)
					return this;

				return m_Parent.FindTopMostParent();
			}

			public void ZSort()
			{
				m_Children = m_Children.OrderBy(c => c.zIndex).ToList();
			}

			public bool IsCollapsed()
			{
				if ((Caps & Capabilities.DoesNotCollapse)==Capabilities.DoesNotCollapse)
					return false;

				return collapsed;
			}

			public Rect elementRect
			{
				get
				{
					Rect rect = new Rect();
					rect.xMin = m_Translation.x;
					rect.yMin = m_Translation.y;
					rect.xMax = rect.xMin + m_Scale.x;
					rect.yMax = rect.yMin + m_Scale.y;
					return rect;
				}
			}

			public virtual Rect boundingRect
			{
				get
				{
					Rect rect = new Rect();
					rect.xMin = m_Translation.x;
					rect.yMin = m_Translation.y;
					rect.xMax = rect.xMin + m_Scale.x;
					rect.yMax = rect.yMin + m_Scale.y;

					foreach (CanvasElement e in m_Children)
					{
						Rect childRect = e.boundingRect;
						childRect.x += rect.x;
						childRect.y += rect.y;
						rect = RectUtils.Encompass(rect, childRect);
					}

					return rect;
				}
			}

			public Rect canvasBoundingRect
			{
				get
				{
					Rect currentRect = boundingRect;
					CanvasElement p = Parent;
					while (p != null)
					{
						if (p is Canvas2D)
							break;

						currentRect.x += p.boundingRect.x;
						currentRect.y += p.boundingRect.y;
						p = p.Parent;
					}
					return currentRect;
				}
			}

			public int zIndex
			{
				get { return m_zIndex; }
				set { m_zIndex = value; }
			}

			public Texture2D texture
			{
				get { return m_Texture; }
			}

			public Vector3 translation
			{
				get { return m_Translation; }
				set { m_Translation = value; }
			}

			public Vector3 scale
			{
				get { return m_Scale; }
				set { m_Scale = value; }
			}

			public virtual bool Contains(Vector2 point)
			{
				return canvasBoundingRect.Contains(point);
			}

			public virtual bool Intersects(Rect rect)
			{
				if (RectUtils.Contains(rect, canvasBoundingRect))
				{
					return true;
				}
				else if (canvasBoundingRect.Overlaps(rect))
				{
					return true;
				}
				return false;
			}

			public virtual void DeepInvalidate()
			{
				m_Dirty = true;
				foreach (CanvasElement e in m_Children)
				{
					e.DeepInvalidate();
				}
	        
				foreach (CanvasElement d in m_Dependencies)
				{
					d.DeepInvalidate();
				}
			}

			public void Invalidate()
			{
				m_Dirty = true;

				foreach (CanvasElement d in m_Dependencies)
				{
					d.Invalidate();
				}

				CanvasElement theParent = Parent;

           		if (theParent == null || (theParent is Canvas2D))
					return;

				while (theParent.Parent != null && !(theParent.Parent is Canvas2D))
				{
					theParent = theParent.Parent;
				}

				theParent.DeepInvalidate();
			}

			public void AddManipulator(IManipulate m)
			{
				m.AttachTo(this);
			}

		    private void CreateTexture()
		    {
		        Rect textureRect = boundingRect;
		        m_Texture = new Texture2D((int) textureRect.width, (int) textureRect.height, TextureFormat.ARGB32, true)
		        {
		            filterMode = FilterMode.Trilinear, 
                    wrapMode = TextureWrapMode.Clamp
		        };
		    }

		    public bool PrepareRender()
		    {
		        if (Event.current.type != EventType.Repaint)
		            return false;

		        if (!m_Dirty || !m_SupportsRenderToTexture)
		            return false;

		        // if null create
		        // if size is differnt destroy / create
		        if (m_Texture == null)
		            CreateTexture();
		        else if ((int) boundingRect.width != m_Texture.width || (int) boundingRect.height != m_Texture.height)
		        {
		            Object.DestroyImmediate(m_Texture);
		            CreateTexture();
		        }

		        Layout();
		        m_Dirty = false;
		        return true;
		    }

		    public void EndRender(float renderTextureHeight)
			{
				Rect textureRect = boundingRect;
				float origin = SystemInfo.graphicsDeviceVersion.StartsWith("Direct") ? 0 : renderTextureHeight - textureRect.height;
				m_Texture.ReadPixels(new Rect(0, origin, textureRect.width, textureRect.height), 0, 0);
				m_Texture.Apply();
			}

			bool Collide(Vector2 p)
			{
				return boundingRect.Contains(p);
			}

			public CanvasElement[] Children()
			{
				return m_Children.ToArray();
			}

			public bool HasDependency<T>()
			{
				foreach (CanvasElement d in m_Dependencies)
				{
					if (d is T)
						return true;
				}
				return false;
			}

			public void AddDependency(CanvasElement e)
			{
				m_Dependencies.Add(e);
			}

			public CanvasElement[] FindChildren<T>()
			{
				List<CanvasElement> filtered = new List<CanvasElement>();
				foreach (CanvasElement e in m_Children)
				{
					CanvasElement[] inner = e.FindChildren<T>();
					filtered.AddRange(inner);

					if (e is T)
					{
						filtered.Add(e);
					}
				}

				return filtered.ToArray();
			}

			public virtual void DebugDraw()
			{
				Handles.DrawSolidRectangleWithOutline(canvasBoundingRect, new Color(1.0f, 0.0f, 0.0f, 0.2f), new Color(1.0f, 0.0f, 0.0f, 0.4f));
				foreach (CanvasElement e in m_Children)
				{
					e.DebugDraw();
				}
			}

			public virtual void Clear()
			{
				m_Texture = null;
				m_Children.Clear();
			}

			public virtual void UpdateModel(UpdateType t)
			{
				foreach (CanvasElement c in m_Children)
				{
					c.UpdateModel(t);
				}
				foreach (CanvasElement e in m_Dependencies)
				{
					e.UpdateModel(t);
				}
			}

			public virtual void AddChild(CanvasElement e)
			{
				e.Parent = this;
				if (!(e.Parent is Canvas2D))
				{
					e.collapsed = collapsed;
				}
				m_Children.Add(e);
			}

			public virtual void Layout()
			{
				foreach (CanvasElement e in m_Children)
				{
					e.Layout();
				}
			}

			public virtual void OnRenderList(List<CanvasElement> visibleList, Canvas2D parent)
			{
				Rect screenRect = new Rect();
				screenRect.min = parent.MouseToCanvas(parent.clientRect.min);
				screenRect.max = parent.MouseToCanvas(new Vector2(Screen.width, Screen.height));
				Rect thisRect = boundingRect;
				foreach (CanvasElement e in visibleList)
				{
					if (e.texture != null)
					{
						float ratio = 1.0f;
						Rect r = new Rect(e.translation.x, e.translation.y, e.texture.width, e.texture.height);
						if (r.y < screenRect.y)
						{
							float overlap = (screenRect.y - r.y);
							r.y = screenRect.y;
							r.height -= overlap;
							if (r.height < 0.0f)
							{
								r.height = 0.0f;
							}
							ratio = r.height / e.texture.height;
						}

						Graphics.DrawTexture(r, e.texture, new Rect(0, 0, 1.0f, ratio), 0, 0, 0, 0);
					}
					else
					{
						e.Render(thisRect, parent);
					}

					e.RenderWidgets(parent);
				}

				if (OnWidget != null)
					OnWidget(this, Event.current, parent);
			}

			private void RenderWidgets(Canvas2D parent)
			{
				if (OnWidget != null)
				{
					OnWidget(this, Event.current, parent);
				}

				foreach (CanvasElement e in m_Children)
				{
					e.RenderWidgets(parent);
				}
			}

			public virtual bool DispatchEvents(Event evt, Canvas2D parent)
			{
				foreach (CanvasElement e in m_Children)
				{
					if (e.DispatchEvents(evt, parent) == true)
						return true;
				}

				return FireEvents(evt, parent, this);
			}

			public bool FireEvents(Event evt, Canvas2D parent, CanvasElement target)
			{
				if (parent != this && (evt.type == EventType.MouseDown))
				{
					if (!Contains(parent.MouseToCanvas(evt.mousePosition)))
					{
						return false;
					}
				}

				if (target == null)
				{
					target = this;
				}

				bool handled = false;
				if (AllEvents != null)
				{
					bool wasNotUsed = evt.type != EventType.Used;

					AllEvents(target, evt, parent);
					if (wasNotUsed && evt.type == EventType.Used)
					{
						parent.LogInfo("AllEvent handler on " + target);
					}
				}

				switch (evt.type)
				{
					case EventType.MouseUp: handled = MouseUp == null ? false : MouseUp(target, evt, parent); break;
					case EventType.MouseDown:
					{
						if (evt.clickCount < 2)
						{
							handled = MouseDown == null ? false : MouseDown(target, evt, parent);
							break;
						}
						else
						{
							handled = DoubleClick == null ? false : DoubleClick(target, evt, parent);
							break;
						}
					}
					case EventType.MouseDrag: handled = MouseDrag == null ? false : MouseDrag(target, evt, parent); break;
					case EventType.DragPerform: handled = DragPerform == null ? false : DragPerform(target, evt, parent); break;
					case EventType.DragExited: handled = DragExited == null ? false : DragExited(target, evt, parent); break;
					case EventType.DragUpdated: handled = DragUpdated == null ? false : DragUpdated(target, evt, parent); break;
					case EventType.ScrollWheel: handled = ScrollWheel == null ? false : ScrollWheel(target, evt, parent); break;
					case EventType.KeyDown: handled = KeyDown == null ? false : KeyDown(target, evt, parent); break;
					case EventType.ContextClick: handled = ContextClick == null ? false : ContextClick(target, evt, parent); break;
				}

				return handled;
			}

			public virtual void Render(Rect parentRect, Canvas2D canvas)
			{
				foreach (CanvasElement e in m_Children)
				{
					e.Render(parentRect, canvas);
				}

				if (OnWidget != null)
					OnWidget(this, Event.current, canvas);
			}
		};

		public class Canvas2D : CanvasElement
		{
			protected List<CanvasElement> m_Elements = new List<CanvasElement>();
			protected List<CanvasElement> m_Selection = new List<CanvasElement>();

			private QuadTree<CanvasElement> m_QuadTree = new QuadTree<CanvasElement>();
			private Rect m_CanvasRect = new Rect();
			private Vector2 m_ViewOffset = new Vector2();
			private Vector2 m_ViewOffsetUnscaled = new Vector2();
			private bool m_ShowDebug = false;
			private string m_DebugEventName = "";
			private RenderTexture m_renderTexture = null;
			private float m_ScreenHeightOffset = 12;
			public Rect debugRect = new Rect();
			public event CanvasEvent OnBackground;
			public event CanvasEvent OnOverlay;
			private EditorWindow m_HostWindow = null;
			private ICanvasDataSource m_DataSource = null;
			private Rect m_ClientRectangle = new Rect();
			private ModalWindowProc m_CurrentModalWindow = null;
			private Rect m_CurrentModalWindowRect = new Rect();

			public ICanvasDataSource dataSource
			{
				get { return m_DataSource; }
				set { m_DataSource = value; }
			}

			internal class CaptureSession
			{
				public CanvasElement m_Callbacks = null;
				public List<CanvasElement> m_Targets = new List<CanvasElement>();
				public IManipulate m_Manipulator = null;
				public bool m_IsRunning = false;
				public bool m_IsEnding = false;
			}

			private CaptureSession m_CaptureSession = null;

			public Rect clientRect
			{
				get
				{
					return m_ClientRectangle;
				}
			}

			public Vector2 viewOffset
			{
				get { return m_ViewOffset; }
			}

			public Rect CanvasRect
			{
				get { return m_CanvasRect; }
			}

			public List<CanvasElement> Elements
			{
				get { return m_Children; }
			}

			public List<CanvasElement> Selection
			{
				get { return m_Selection; }
			}

			public bool ShowQuadTree
			{
				get { return m_ShowDebug; }
				set { m_ShowDebug = value; }
			}

			public void ReleaseTextures()
			{
				if (m_renderTexture)
				{
					m_renderTexture.Release();
					m_renderTexture = null;
				}
			}

			public void ReCreateRenderTexture()
			{
				if (m_renderTexture)
				{
					if (m_renderTexture.IsCreated())
						return;

					m_renderTexture.Release();
					m_renderTexture = null;
				}

				m_renderTexture = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32);
				m_renderTexture.useMipMap = true;
				m_renderTexture.generateMips = true;
				m_renderTexture.antiAliasing = 1;
				m_renderTexture.filterMode = FilterMode.Trilinear;
				m_renderTexture.Create();
			}

			
			public void ReloadData()
			{
				if (m_DataSource == null)
					return;
				Clear();
				CanvasElement[] elems = m_DataSource.FetchElements();
				foreach (var e in elems)
					AddChild(e);
				ZSort();
			}

			public override void Clear()
			{
				if (m_Elements != null)
					m_Elements.Clear();

				if (m_Selection != null)
					m_Selection.Clear();

				if (m_QuadTree != null)
					m_QuadTree.Clear();

				m_Children.Clear();
				if (m_Children != null)
				{
					/*foreach (CanvasElement e in m_Children)
					{
						e.Clear();
					}
					m_Children.Clear();*/
				}
			}

			public override void DeepInvalidate()
			{
				foreach (CanvasElement e in m_Children)
				{
					e.DeepInvalidate();
				}
			}

			public Vector2 MouseToCanvas(Vector2 lhs)
			{
				return new Vector2((lhs.x - m_Translation.x)/m_Scale.x, (lhs.y - m_Translation.y)/m_Scale.y) + (m_ViewOffset/2.0f);
			}

			public Vector2 CanvasToScreen(Vector2 lhs)
			{
				return new Vector2((lhs.x*m_Scale.x) + m_Translation.x, (lhs.y*m_Scale.y) + m_Translation.y);
			}

			public Rect CanvasToScreen(Rect r)
			{
				Vector3 t = m_Translation;
				t -= new Vector3(m_ViewOffsetUnscaled.x / 2.0f, m_ViewOffsetUnscaled.y / 2.0f, 0.0f);
				Matrix4x4 mm = Matrix4x4.TRS(t, Quaternion.identity, m_Scale);
				

				Vector3 offset = new Vector3(0.0f, 0.0f, 0.0f);

				Vector3[] points =
				{
					new Vector3(r.xMin, r.yMin, 0.0f),
					new Vector3(r.xMax, r.yMin, 0.0f),
					new Vector3(r.xMax, r.yMax, 0.0f),
					new Vector3(r.xMin, r.yMax, 0.0f)
				};

				for (int a = 0; a < 4; a++)
				{
					points[a] = mm.MultiplyPoint(points[a]);
					points[a] += offset;
				}

				return new Rect(points[0].x, points[0].y, points[2].x - points[0].x, points[2].y - points[0].y);
			}

			public Canvas2D(Object target, EditorWindow host, ICanvasDataSource dataSource)
			{
				MouseDown += NoOp;
				MouseUp += NoOp;
				DoubleClick += NoOp;
				ScrollWheel += NoOp;
				m_HostWindow = host;
				m_DataSource = dataSource;

				ReCreateRenderTexture();
			}

			public void Repaint()
			{
				m_HostWindow.Repaint();
			}

			private bool NoOp(CanvasElement element, Event e, Canvas2D parent)
			{
				return true;
			}

			public void OnGUI(EditorWindow parent, Rect clientRectangle)
			{
				m_ClientRectangle = clientRectangle;

				Event evt = Event.current;

				/*Rect canvasGLArea = clientRectangle;
				canvasGLArea.height += canvasGLArea.y;
				GL.Viewport(canvasGLArea);*/

				if (evt.type == EventType.Repaint)
				{
					ReCreateRenderTexture();

					if (OnBackground != null)
						OnBackground(this, Event.current, this);

					OnRender(parent, clientRectangle);

					if (OnOverlay != null)
						OnOverlay(this, Event.current, this);

					if (m_CurrentModalWindow != null)
					{
						Handles.DrawSolidRectangleWithOutline(m_CurrentModalWindowRect, new Color(0.22f, 0.22f, 0.22f, 1.0f),new Color(0.22f, 0.22f, 0.22f, 1.0f));
						GUI.BeginGroup(m_CurrentModalWindowRect);
						m_CurrentModalWindow(this);
						GUI.EndGroup();
					}

					//m_ShowDebug = GUI.Toggle(new Rect(10, 55, 200, 50), m_ShowDebug, "Debug Info");

					return;
				}

				if (evt.isMouse || evt.isKey)
				{
					if (!clientRectangle.Contains(evt.mousePosition))
						return;
				}

				//m_ShowDebug = GUI.Toggle(new Rect(10, 55, 200, 50), m_ShowDebug, "Debug Info");

				// sync selection globally on MouseUp and KeyEvents
				bool syncSelection = false;
				if (evt.type == EventType.MouseUp || evt.isKey)
				{
					syncSelection = true;
				}
				
				OnEvent(evt);

				if (syncSelection)
				{
					SyncUnitySelection();
				}
			}

			private void SyncUnitySelection()
			{
				List<Object> targets = new List<Object>();

				foreach (CanvasElement se in m_Selection)
				{
					if (se.Target != null)
					{
						targets.Add(se.Target);
					}
				}
				if (targets.Count() == 1)
				{
					UnityEditor.Selection.activeObject = targets[0];
				}
				else if (targets.Count() > 1)
				{
					UnityEditor.Selection.activeObject = null;
				}
				else if (targets.Count() == 0)
				{
					UnityEditor.Selection.activeObject = Target;
				}
			}

			private void OnRender(EditorWindow parent, Rect clientRectangle)
			{
				m_ScreenHeightOffset = clientRectangle.y - 42;
				// query quad tree for the list of elements visible
				Rect screenRect = new Rect();
				screenRect.min = MouseToCanvas(new Vector2(0.0f, 0.0f));
				screenRect.max = MouseToCanvas(new Vector2(Screen.width, Screen.height));

				List<CanvasElement> visibleElements = m_QuadTree.ContainedBy(screenRect).OrderBy(c => c.zIndex).ToList();

				// update render textures
				RenderTexture prev = RenderTexture.active;
				RenderTexture.active = m_renderTexture;
				Rect canvasRect = boundingRect;
				foreach (CanvasElement e in visibleElements)
				{
					if (e.PrepareRender())
					{
						GL.Clear(true, true, new Color(0, 0, 0, 0));
						GUI.BeginClip(new Rect(0, 0, e.boundingRect.width, e.boundingRect.height), Vector2.zero, Vector2.zero, true);
						e.Render(canvasRect, this);
						GUI.EndGroup();

						e.EndRender(m_renderTexture.height);
					}
				}
				RenderTexture.active = prev;

				Rect extents = clientRectangle;
				Matrix4x4 m = GUI.matrix;
				//m_Scale = Vector3.one;
				GUI.matrix = Matrix4x4.TRS(m_Translation, Quaternion.identity, m_Scale);

				m_ViewOffset = new Vector2(0.0f, -(extents.yMin - m_ScreenHeightOffset)*(1.0f/m_Scale.y));
				m_ViewOffsetUnscaled = new Vector2(0.0f, -(extents.yMin - m_ScreenHeightOffset));
				GUI.BeginClip(extents, Vector2.zero, m_ViewOffset, true);

				if (m_ShowDebug)
				{
					m_QuadTree.DebugDraw();
				}

				OnRenderList(visibleElements, this);

				//m_ShowDebug = true;
				if (m_ShowDebug)
				{
					foreach (CanvasElement e in visibleElements)
					{
						e.DebugDraw();
					}
				}

				GUI.EndClip();

				GUI.matrix = m;

				if (m_ShowDebug)
				{
					Color c = GUI.color;
					GUI.color = new Color(1.0f, 0.5f, 0.0f, 1.0f);
					GUI.Label(new Rect(10, 75, 200, 32), "elements rendered:" + visibleElements.Count, GUIStyle.none);
					GUI.Label(new Rect(10, 100, 200, 32), "last event:" + m_DebugEventName, GUIStyle.none);
					
					if (m_CaptureSession != null)
					{
						string typesCaptured = "";
						foreach (CanvasElement e in m_CaptureSession.m_Targets)
						{
							typesCaptured += " [" + e.GetType().ToString() + "]";
						}
						GUI.Label(new Rect(10, 85, 200, 32),
							m_CaptureSession.m_Manipulator.GetType().ToString() + " captured elements: " + m_CaptureSession.m_Targets.Count +
							" " + typesCaptured, GUIStyle.none);
					}
					GUI.color = c;

					Handles.DrawSolidRectangleWithOutline(debugRect, new Color(1.0f, 0.5f, 0.0f, 1.0f), new Color(1.0f, 0.5f, 0.0f, 1.0f));

				}
			}

			public override void AddChild(CanvasElement e)
			{
				base.AddChild(e);
				RebuildQuadTree();
			}

			public void RebuildQuadTree()
			{
				if (m_Children.Count == 0)
					return;

				m_CanvasRect = m_Children[0].boundingRect;
				foreach (CanvasElement c in m_Children)
				{
					Rect childRect = c.boundingRect;
					childRect = RectUtils.Inflate(childRect, 1.1f);
					m_CanvasRect = RectUtils.Encompass(m_CanvasRect, childRect);
				}

				m_QuadTree.SetSize(m_CanvasRect);
				m_QuadTree.Insert(m_Children);
			}

			public bool OnEvent(Event evt)
			{
				bool logEvent = false;
				if ((evt.type != EventType.Repaint) && (evt.type != EventType.Layout))
				{	
					logEvent = (!evt.isMouse) && m_ShowDebug;;
					m_DebugEventName = evt.type.ToString();
				}

				// if user clicks outside the current modal window, dismiss it
				if (evt.type == EventType.MouseDown && m_CurrentModalWindow != null)
				{
					if (!m_CurrentModalWindowRect.Contains(evt.mousePosition))
					{
						m_CurrentModalWindow = null;
					}
				}

				if (m_CurrentModalWindow != null)
				{
					if (logEvent)
					{
						m_DebugEventName += " handled by modal window";
					}

					GUI.BeginGroup(m_CurrentModalWindowRect);
					m_CurrentModalWindow(this);
					GUI.EndGroup();
					return true;
				}
				// select elements that will receive the events
				// 1- Captured elements have precedence
				// 2- Then the selection
				// 3- If nothing is captured or selected, we raycast into the quadtree

				if (m_CaptureSession != null)
				{
					if (RunCaptureSession(evt))
					{
						if (logEvent)
						{
							m_DebugEventName += " handled by capture session\n";
						}
						evt.Use();
						return true;
					}
					return false;
				}

				List<CanvasElement> elems = m_Selection.ToArray().ToList();

				// special case for clicking outside of the selection
				if (evt.type == EventType.MouseDown)
				{
					bool collidesWithSelection = false;
					foreach (CanvasElement e in elems)
					{
						if (e.Contains(MouseToCanvas(evt.mousePosition)))
						{
							collidesWithSelection = true;
							break;
						}
					}
					if (!collidesWithSelection)
					{
						if (m_Selection.Count() > 0)
						{
							ClearSelection();
						}
						//EditorGUI.EndEditingActiveTextField();
						elems.Clear();
					}
				}

				if (elems.Count == 0)
				{
					Vector2 canvasPosition = MouseToCanvas(evt.mousePosition);
					Rect mouseRect = new Rect(canvasPosition.x, canvasPosition.y, 10, 10);
					elems = m_QuadTree.ContainedBy(mouseRect);
				}

				EventType originalEvent = evt.type;
				bool usedByAtLeastOneChildren = false;
				foreach (CanvasElement e in elems)
				{
					if (e != this)
					{
						e.DispatchEvents(evt, this);

						// if the event was consumed by an element and we are not multiselecting, they we stop
						// propagation, otherwise we continue propagating the original event
						if (evt.type == EventType.Used && m_Selection.Count == 0)
						{
							if (logEvent == true)
								m_DebugEventName += " propagation stopped";
							return true;
						}
						else if (evt.type == EventType.Used)
						{
							usedByAtLeastOneChildren = true;
							evt.type = originalEvent;
						}
					}
				}

				if (usedByAtLeastOneChildren)
				{
					if (logEvent == true)
						m_DebugEventName += " used by children";
					evt.Use();
					return true;
				}

				if (logEvent == true)
					m_DebugEventName += " was ignored by all children, event falls back to main canvas";
				// event was not handled by any of our children, so we fallback to ourselves (the main canvas2D)
				base.FireEvents(evt, this, this);
				return false;
			}

			public void LogInfo(string info)
			{
				if (m_ShowDebug)
				{
					m_DebugEventName += info;
				}
			}
			private bool RunCaptureSession(Event evt)
			{
				m_CaptureSession.m_IsRunning = true;
				EventType originalEvent = evt.type;
				bool wasUsed = false;
				foreach (CanvasElement e in m_CaptureSession.m_Targets)
				{
					m_CaptureSession.m_Callbacks.FireEvents(evt, this, e);
					if (evt.type == EventType.Used)
					{
						wasUsed = true;
					}
					evt.type = originalEvent;

				}
				if (wasUsed)
				{
					evt.Use();
				}
				m_CaptureSession.m_IsRunning = false;
				if (m_CaptureSession.m_IsEnding)
				{
					EndCapture();
				}
				return wasUsed;
			}

			public void StartCapture(IManipulate manipulator, CanvasElement e)
			{
				m_CaptureSession = new CaptureSession();
				m_CaptureSession.m_Manipulator = manipulator;
				m_CaptureSession.m_Callbacks = new CanvasElement();
				manipulator.AttachTo(m_CaptureSession.m_Callbacks);
				if (m_Selection.Count > 0 && manipulator.GetCaps(ManipulatorCapability.eMultiSelection) == true)
				{
					m_CaptureSession.m_Targets.AddRange(m_Selection);
				}
				else
				{
					m_CaptureSession.m_Targets.Add(e);
				}
			}

			public void EndCapture()
			{
				if (m_CaptureSession == null)
					return;
				if (m_CaptureSession.m_IsRunning == false)
				{
					foreach (CanvasElement e in m_CaptureSession.m_Targets)
					{
						e.UpdateModel(UpdateType.eUpdate);
					}

					m_CaptureSession = null;
					RebuildQuadTree();
					return;
				}

				m_CaptureSession.m_IsEnding = true;
			}

			public bool IsCaptured(IManipulate manipulator)
			{
				return m_CaptureSession == null ? false : m_CaptureSession.m_Manipulator == manipulator;
			}

			public void AddToSelection(CanvasElement e)
			{
				if (e is Canvas2D)
					return;

				e.selected = true;
				m_Selection.Add(e);
			}

			public void ClearSelection()
			{
				foreach (CanvasElement e in m_Selection)
				{
					e.selected = false;
				}

				m_Selection.Clear();
				RebuildQuadTree();
			}

			public CanvasElement[] Pick<T>(Rect area)
			{
				List<CanvasElement> elems = m_QuadTree.ContainedBy(area);
				List<CanvasElement> returnedElements = new List<CanvasElement>();

				foreach (CanvasElement e in elems)
				{
					CanvasElement[] allTs = e.FindChildren<T>();
					foreach (CanvasElement c in allTs)
					{
						returnedElements.Add(c);
					}
				}

				return returnedElements.ToArray();
			}

			public CanvasElement PickSingle<T>(Vector2 position)
			{
				Vector2 canvasPosition = MouseToCanvas(position);
				Rect mouseRect = new Rect(canvasPosition.x, canvasPosition.y, 10, 10);
				List<CanvasElement> elems = m_QuadTree.ContainedBy(mouseRect);
				foreach (CanvasElement e in elems)
				{
					CanvasElement[] allTs = e.FindChildren<T>();
					foreach (CanvasElement c in allTs)
					{
						if (c.Contains(canvasPosition))
						{
							return c;
						}
					}
				}

				return null;
			}

			public void RunModal(Rect rect, ModalWindowProc mwp )
			{
				if (m_CurrentModalWindow != null)
					return;
				m_CurrentModalWindow = mwp;
				m_CurrentModalWindowRect = rect;
			}

			public void EndModal()
			{
				m_CurrentModalWindow = null;
				Repaint();
			}
		}
	}
}