﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.ManagedSpy;
using System.Diagnostics;
using System.Threading;
using O2.DotNetWrappers.ExtensionMethods;

namespace ManagedSpy
{
        using O2.XRules.Database.Utils;

    /// <summary>
            /// This is the main window of ManagedSpy.
        /// Its a fairly simple Form containing a TreeView and TabControl.
        /// The TreeView contains processes and thier windows
        /// The TabControl contains properties and events.
        /// </summary>
        /// 
    public partial class MainGui : UserControl
    {
        
        public ControlProxy currentProxy = null;
        EventFilterDialog dialog = new EventFilterDialog();

        public MainGui()  
        {   
            InitializeComponent(); 
            //adding O2              
            this.add_ExtraMenuItems();

            var vsModules = (from frame in new StackTrace().GetFrames()
                              let module = frame.GetMethod().Module
                             where module.Name.contains("VisualStudio")
                             select module.Name).distinct();
            if (vsModules.notEmpty())
            { 
                var callbackFromVs = (Action<Type>)"onMainGuiCtor".o2Cache();
                callbackFromVs(this.type());            
            }
        }  

        public void exitToolStripMenuItem_Click(object sender, EventArgs e) {
            Application.Exit();
        }

        
         
        public void refreshToolStripMenuItem_Click(object sender, EventArgs e) {
            RefreshWindows();
        }

        /// <summary>
        /// This rebuilds the window hierarchy
        /// </summary>
        public void RefreshWindows() {
            this.treeWindow.BeginUpdate();
            this.treeWindow.Nodes.Clear();
            ControlProxy[] topWindows = Microsoft.ManagedSpy.ControlProxy.TopLevelWindows;
            if (topWindows != null && topWindows.Length > 0) {
                foreach (ControlProxy cproxy in topWindows) {
                    TreeNode procnode;

                    //only showing managed windows
                    if (this.ShowNative.Checked || cproxy.IsManaged) {
                        Process proc = cproxy.OwningProcess;
                        if (proc != null && proc.Id != Process.GetCurrentProcess().Id) {
                            procnode = treeWindow.Nodes[proc.Id.ToString()];
                            if (procnode == null) {
                                procnode = treeWindow.Nodes.Add(proc.Id.ToString(),
                                    proc.ProcessName +
                                    "  " + proc.MainWindowTitle + 
                                    " [" + proc.Id.ToString() + "]");
                                procnode.Tag = proc;
                            }
                            string name = String.IsNullOrEmpty(cproxy.GetComponentName()) ?
                                "<noname>" : cproxy.GetComponentName();
                            TreeNode node = procnode.Nodes.Add(cproxy.Handle.ToString(), 
                                name + 
                                "     [" +
                                cproxy.GetClassName() + 
                                "]");
                            node.Tag = cproxy;
                        }
                    }
                }
            }
            if (treeWindow.Nodes.Count == 0) {
                treeWindow.Nodes.Add("No managed processes running.");
                treeWindow.Nodes.Add("Select View->Refresh.");
            }
            this.treeWindow.EndUpdate();
        }

        /// <summary>
        /// Called when the user selects a control in the treeview
        /// </summary>
        public void treeWindow_AfterSelect(object sender, TreeViewEventArgs e) {
            this.propertyGrid.SelectedObject = this.treeWindow.SelectedNode.Tag;
            this.toolStripStatusLabel1.Text = treeWindow.SelectedNode.Text;
            StopLogging();
            this.eventGrid.Rows.Clear();
            StartLogging();
        }

        /// <summary>
        /// This is called when the selected ControlProxy raises an event
        /// </summary>
        public void ProxyEventFired(object sender, ProxyEventArgs args) {
            eventGrid.FirstDisplayedScrollingRowIndex = this.eventGrid.Rows.Add(new object[] { args.eventDescriptor.Name, args.eventArgs.ToString() });
        }

        /// <summary>
        /// Used to build the treeview as the user expands nodes.
        /// We always stay one step ahead of the user to get the expand state set correctly.
        /// So, for instance, when we just show processes, we have already calculated all the top level windows.
        /// When the user expands a process -- we calculate the children of all top level windows
        /// And so on...
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void treeWindow_BeforeExpand(object sender, TreeViewCancelEventArgs e) {
            foreach (TreeNode child in e.Node.Nodes) {
                child.Nodes.Clear();
                ControlProxy proxy = child.Tag as ControlProxy;
                if (proxy != null) {
                    foreach (ControlProxy proxychild in proxy.Children) {
                        string name = String.IsNullOrEmpty(proxychild.GetComponentName()) ?
                            "<noname>" : proxychild.GetComponentName();
                        TreeNode node = child.Nodes.Add(proxychild.Handle.ToString(), name + "     [" +
                            proxychild.GetClassName() + "]");
                        node.Tag = proxychild;
                    }
                }
            }
        }

        public void flashWindow_Click(object sender, EventArgs e) {
            FlashCurrentWindow();
        }
        public void showWindowToolStripMenuItem_Click(object sender, EventArgs e) {
            FlashCurrentWindow();
        }
        /// <summary>
        /// This uses ControlPaint.DrawReversibleFrame to highlight the given window
        /// </summary>
        public void FlashCurrentWindow() {
            ControlProxy proxy = propertyGrid.SelectedObject as ControlProxy;
            if (proxy != null && proxy.IsManaged && proxy.GetValue("Location") != null) {

                IntPtr handle = proxy.Handle;
                Point topleft = (Point)proxy.GetValue("Location");
                if (proxy.Parent != null) {
                    topleft = (Point)proxy.Parent.PointToScreen(topleft);
                }
                Size size = (Size)proxy.GetValue("Size");
                Rectangle r = new Rectangle(topleft, size);

                for (int i = 1; i <= 7; i++) {
                    ControlPaint.DrawReversibleFrame(r, Color.Red, FrameStyle.Thick);
                    Thread.Sleep(100);
                }
                Thread.Sleep(250);  //extra delay at the end.
                ControlPaint.DrawReversibleFrame(r, Color.Red, FrameStyle.Thick);
            }
        }

        /// <summary>
        /// Starts event logging
        /// </summary>
        public void StartLogging() {
            if (tsButtonStartStop.Checked) {
                currentProxy = propertyGrid.SelectedObject as ControlProxy;
                if (currentProxy != null) {
                    //unsubscribe from events.
                    foreach (EventDescriptor ed in currentProxy.GetEvents()) {
                        if (dialog.EventList[ed.Name].Display) {
                            currentProxy.SubscribeEvent(ed);
                        }
                    }
                    currentProxy.EventFired += new ControlProxyEventHandler(ProxyEventFired);
                }
            }
        }   

        /// <summary>
        /// Stops event Logging
        /// </summary>
        public void StopLogging() {
            if (currentProxy != null) {
                //unsubscribe from events.
                foreach (EventDescriptor ed in currentProxy.GetEvents()) {
                    currentProxy.UnsubscribeEvent(ed);
                }
                currentProxy.EventFired -= new ControlProxyEventHandler(ProxyEventFired);
            }
        }

        public void tsButtonStartStop_Click(object sender, EventArgs e) {
            StopLogging();
            StartLogging();
            if (tsButtonStartStop.Checked) {
                tsButtonStartStop.Image = ManagedSpy.Properties.Resources.Stop;
            }
            else {
                tsButtonStartStop.Image = ManagedSpy.Properties.Resources.Play;
            }
        }

        

        public void tsbuttonRefresh_Click(object sender, EventArgs e) {
            RefreshWindows();
        }

        public void tsButtonClear_Click(object sender, EventArgs e) {
            this.eventGrid.Rows.Clear();
        }

        public void tsbuttonFilterEvents_Click(object sender, EventArgs e) {
            dialog.ShowDialog();
            StopLogging();
            StartLogging();
        }

        public void filterEventsToolStripMenuItem_Click(object sender, EventArgs e) {
            dialog.ShowDialog();
            StopLogging();
            StartLogging();
        }

        public void aboutManagedSpyToolStripMenuItem_Click(object sender, EventArgs e) {
            HelpAbout about = new HelpAbout();
            about.ShowDialog();
        }
    }

    
}
