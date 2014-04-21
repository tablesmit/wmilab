﻿namespace WMILab
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Management;
    using System.Management.CodeGeneration;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;
    using ScintillaNET;
    using WMILab.Localization;

    public partial class frmMain : Form
    {
        #region Constructors

        public frmMain()
        {
            InitializeComponent();

            InitCodeEditor();

            // Connection and enumeration options
            classListEnumOpts.EnumerateDeep = true;
            classObjGetOpts.UseAmendedQualifiers = true;

            // Observer event handlers
            nsTreeObserver.ObjectReady += new ObjectReadyEventHandler(OnNamespaceReady);

            classListObserver.ObjectReady += new ObjectReadyEventHandler(OnClassReady);
            classListObserver.Completed += new CompletedEventHandler(OnClassSearchCompleted);

            // Refresh script template menu
            this.RefreshCodeGeneratorMenu();
        }

        private void OnFormShown(object sender, EventArgs e)
        {
            resetClassColumnHeader();

            // Build local namespace tree
            this.RefreshNamespaceTree();

            // Navigate to default namespace and class
            this.CurrentNamespacePath = @"\\.\ROOT\CIMV2";
            this.CurrentClassPath = @"\\.\ROOT\CIMV2:Win32_ComputerSystem";
            this.CodeGenerator = new CodeGenerators.VBScript.VbBasicConsoleCodeGenerator();
        }

        #endregion

        #region Fields

        private Scintilla txtCode = new Scintilla();

        private ConnectionOptions conOpts = new ConnectionOptions();

        private String currentNamespacePath;

        private ManagementScope currentNamespaceScope;

        private ManagementClass currentClass;

        private ManagementScope nsTreeScope;

        private TreeNode nsTreeRootNode;

        private ManagementOperationObserver nsTreeObserver = new ManagementOperationObserver();

        private ManagementOperationObserver classListObserver = new ManagementOperationObserver();

        private static ObjectGetOptions nsObjGetOpts = new ObjectGetOptions();

        private static ObjectGetOptions classObjGetOpts = new ObjectGetOptions();

        private static EnumerationOptions nsTreeEnumOpts = new EnumerationOptions();

        private static EnumerationOptions classListEnumOpts = new EnumerationOptions();

        private List<ListViewItem> classListItems = new List<ListViewItem>();

        private bool updatingNavigation = false;

        private ManagementQueryBroker queryBroker;

        private ICodeGenerator codeGenerator;

        private Boolean showSystemClasses = false;

        private Boolean showMappedValues = true;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the full path of the WMI Namepace currently displayed in the main window.
        /// </summary>
        private String CurrentNamespacePath
        {
            get
            {
                return currentNamespacePath;
            }

            set
            {
                if (!String.IsNullOrEmpty(currentNamespacePath) && currentNamespacePath.Equals(value))
                    return;

                // Cancel any running queries
                if (this.queryBroker != null)
                    this.queryBroker.Cancel();

                try
                {
                    this.currentNamespaceScope = new ManagementScope(value, this.conOpts);
                    this.CurrentNamespaceScope.Connect();

                    currentNamespacePath = value;

                    RefreshClassList();
                }

                catch (ManagementException e)
                {
                    this.Log(LogLevel.Critical, String.Format("Error connecting to \"{0}\": {1}", value, e.Message));
                }
            }
        }

        /// <summary>
        /// Gets the ManagementScope for the WMI Namespace currently displayed in the main window.
        /// </summary>
        private ManagementScope CurrentNamespaceScope
        {
            get { return this.currentNamespaceScope; }
        }

        /// <summary>
        /// Gets or set the full path of the WMI Class currently displayed in the main window.
        /// </summary>
        private String CurrentClassPath
        {
            get { return this.currentClass == null ? null : this.currentClass.Path.Path; }

            set
            {
                if (String.IsNullOrEmpty(value))
                {
                    this.CurrentClass = null;
                    return;
                }

                var path = new ManagementPath(value);
                try
                {
                    this.CurrentClass = new ManagementClass(this.CurrentNamespaceScope, path, classObjGetOpts);
                }
                catch (Exception e)
                {
                    this.Log(LogLevel.Critical, String.Format("Unable to load class {0}: {1}", value, e.Message));
                }
            }
        }

        /// <summary>
        /// Gets or sets the WMI Class currently displayed in the main window.
        /// </summary>
        private ManagementClass CurrentClass
        {
            get { return this.currentClass; }

            set
            {
                this.currentClass = value;
                RefreshClassView();
                this.Log(LogLevel.Information, String.Format("Loaded class: {0}", value));
            }
        }

        /// <summary>
        /// Gets or sets the code generator currently used to generate code in the class code tab.
        /// </summary>
        private ICodeGenerator CodeGenerator
        {
            get { return this.codeGenerator; }

            set
            {
                this.codeGenerator = value;
                if(this.CurrentClass != null)
                    this.RefreshScript(this.CurrentClass);
            }
        }

        #endregion

        #region Namespace Tree

        /// <summary>
        /// Refreshes the namespace tree with tree nodes for the currently connected server.
        /// </summary>
        private void RefreshNamespaceTree() {
            this.nsTreeObserver.Cancel();

            var path = new ManagementPath(@"\\localhost\ROOT");
            nsTreeScope = new ManagementScope(path, conOpts);

            var searchPath = new ManagementPath(String.Format(@"{0}:__NAMESPACE", path.Path));
            ManagementClass ns = new ManagementClass(nsTreeScope, searchPath, nsObjGetOpts);

            // TODO: Get hostname of system

            // Create classNode node
            var nodePath = String.Format(@"\\{0}\{1}",ns.Path.Server, ns.Path.NamespacePath);
            nsTreeRootNode = new TreeNode(nodePath);
            nsTreeRootNode.Tag = nodePath;
            nsTreeRootNode.ImageKey = "Home";
            nsTreeRootNode.SelectedImageKey = "Home";
            this.treeViewNamespaces.Nodes.Add(nsTreeRootNode);
            nsTreeRootNode.Expand();

            // Search for namespaces asyncronously
            ns.GetInstances(nsTreeObserver, nsTreeEnumOpts);
        }

        void OnNamespaceReady(object sender, ObjectReadyEventArgs e)
        {
            var name = (String)e.NewObject.Properties["Name"].Value;
            var path = String.Format(@"\\{0}\{1}\{2}", e.NewObject.ClassPath.Server, e.NewObject.ClassPath.NamespacePath, name);
            var parts = path.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);

            // Get parent node from tree
            var node = nsTreeRootNode;
            for (int i = 2; i < parts.Length - 1; i++)
            {
                bool foundNode = false;
                foreach (TreeNode child in node.Nodes)
                {
                    if (child.Text.Equals(parts[i]))
                    {
                        foundNode = true;
                        node = child;
                        break;
                    }
                }

                if (!foundNode)
                    throw new InvalidOperationException(String.Format("TreeNode not found: \"{0}\"", parts[i]));
            }

            var nextNode = new TreeNode(name);
            nextNode.Tag = path;
            nextNode.ImageKey = "NameSpace";
            nextNode.SelectedImageKey = "NameSpaceOpen";

            appendTreeNode(node, nextNode);

            // Search for child namespaces asyncronously
            var searchPath = new ManagementPath(String.Format(@"{0}:__NAMESPACE", path));
            var searchScope = new ManagementScope(path, conOpts);
            ManagementClass ns = new ManagementClass(searchScope, searchPath, nsObjGetOpts);

            try
            {
                ns.GetInstances(nsTreeObserver, nsTreeEnumOpts);
            }
            catch (ManagementException)
            { }
        }

        private delegate void appendTreeNodeDelegate(TreeNode parent, TreeNode child);

        /// <summary>
        /// Appends one tree node to another in a thread safe manner.
        /// </summary>
        /// <param name="parent">The parent TreeNode to be appended to.</param>
        /// <param name="child">The child TreeNode to be appended.</param>
        private void appendTreeNode(TreeNode parent, TreeNode child)
        {
            if(this.InvokeRequired) {
                this.Invoke(new appendTreeNodeDelegate(this.appendTreeNode), parent, child);
                return;
            }

            parent.Nodes.Add(child);
            
            // Expand root
            if(parent == this.nsTreeRootNode)
                parent.Expand();
        }

        #endregion

        #region Class list

        /// <summary>
        /// Refreshes the class list for the currently connected WMI Namespace.
        /// </summary>
        private void RefreshClassList()
        {
            classListItems.Clear();
            this.listViewClasses.Items.Clear();

            this.Log(LogLevel.Information, String.Format("Loading class for path: {0}", this.currentNamespacePath));

            var path = new ManagementPath(this.CurrentNamespacePath);
            var ns = new ManagementClass(this.CurrentNamespaceScope, path, nsObjGetOpts);
            try
            {
                ns.GetSubclasses(classListObserver, classListEnumOpts);
                this.Log(LogLevel.Information, "Finished loading classes.");
            }

            catch (ManagementException e)
            {
                var msg = String.Format("An error occurred listing classes in {0}:\r\n\r\n{1}", this.CurrentNamespacePath, e.Message);
                this.Log(LogLevel.Critical, msg);
            }
        }

        /// <summary>
        /// Applies a filter to the class list based on display options and user searching.
        /// </summary>
        void RefreshClassListFilter()
        {
            this.listViewClasses.Items.Clear();

            var filter = this.txtClassFilter.Text;
            var displayList = new List<ListViewItem>(classListItems.Count);
            for (int i = 0; i < classListItems.Count; i++)
            {
                var className = classListItems[i].Text;
                
                bool display = true;

                // Apply user filter
                display &= String.IsNullOrEmpty(filter) || className.ToLowerInvariant().Contains(filter.ToLowerInvariant());

                // Apply system class filter
                display &= this.showSystemClasses || !classListItems[i].Text.StartsWith("__");

                if(display)
                    displayList.Add(classListItems[i]);
            }

            this.listViewClasses.Items.AddRange(displayList.ToArray());

            this.SelectClassListViewItem(this.CurrentClass.ClassPath.ClassName);
        }

        void OnClassReady(object sender, ObjectReadyEventArgs e)
        {
            var c = (ManagementClass)e.NewObject;

            var item = new ListViewItem(c.Path.ClassName, "Class");
            item.Tag = c.Path.Path;

            if (c.IsEvent())
            {
                if (c.IsEvent())
                {
                    item.ImageKey = "EventSystem";
                }
                else
                {
                    item.ImageKey = "Event";
                }
            }
            else if (c.IsPerformanceCounter())
            {
                item.ImageKey = "Performance";
            }

            else if (c.IsSystemClass())
            {
                item.ImageKey = "ClassSystem";
            }

            else if (c.IsAssociation())
            {
                item.ImageKey = "ClassAssoc";
            }

            classListItems.Add(item);
        }

        void OnClassSearchCompleted(object sender, CompletedEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new CompletedEventHandler(this.OnClassSearchCompleted), sender, e);
                return;
            }

            RefreshClassListFilter();
        }

        #endregion

        #region Class view

        /// <summary>
        /// Selects and highlights a class from the class list without changing the current class.
        /// </summary>
        /// <param name="className">The name of the class item to be deleted.</param>
        private void SelectClassListViewItem(String className)
        {
            this.updatingNavigation = true;

            // Select this the current class in the listview
            if (this.listViewClasses.SelectedItems.Count != 1
                || !this.listViewClasses.SelectedItems[0].Text.Equals(className))
            {
                for (int i = 0; i < this.listViewClasses.Items.Count; i++)
                {
                    if (this.listViewClasses.Items[i].Text.Equals(className))
                    {
                        this.listViewClasses.Items[i].Selected = true;
                        this.listViewClasses.Items[i].Focused = true;
                        this.listViewClasses.Items[i].EnsureVisible();
                        break;
                    }
                }
            }

            this.updatingNavigation = false;
        }

        /// <summary>
        /// Refreshes all class view controls to display data for the currently selected class.
        /// </summary>
        /// <remarks>Refreshes the member, query and code tabs.</remarks>
        private void RefreshClassView()
        {
            // Select the class in the list
            this.SelectClassListViewItem(this.CurrentClass.ClassPath.ClassName);

            // Update class view
            var c = this.CurrentClass;
            RefreshClassMembersListView(c);
            RefreshClassMembersDetailView(c);
            RefreshQueryView(c);
            RefreshScript(c);
        }

        /// <summary>
        /// Refreshes the class member list view with details for members of the specified ManagementClass.
        /// </summary>
        /// <param name="c">The System.Management.ManagementClass to be displayed.</param>
        private void RefreshClassMembersListView(ManagementClass c)
        {
            this.treeViewClassMembers.Nodes.Clear();
            
            TreeNode classNode = new TreeNode(c.ClassPath.ClassName);
            classNode.ImageKey = classNode.SelectedImageKey = "Class";
            classNode.Tag = c;
            this.treeViewClassMembers.Nodes.Add(classNode);
            this.treeViewClassMembers.SelectedNode = classNode;

            List<TreeNode> nodes = new List<TreeNode>();

            foreach (var m in c.Methods)
            {
                var item = new TreeNode(m.Name);
                item.Tag = m;
                item.ImageKey = item.SelectedImageKey = m.IsStatic() ? "StaticMethod" : "Method";

                nodes.Add(item);
            }

            foreach (PropertyData prop in c.Properties)
            {
                // Update Tree
                TreeNode node = new TreeNode(prop.Name);
                if (prop.IsKey())
                {
                    node.ImageKey = node.SelectedImageKey = "PropertyKey";
                }
                else
                {
                    node.ImageKey = node.SelectedImageKey = "Property";
                }
                node.Tag = prop;
                nodes.Add(node);
            }

            classNode.Nodes.AddRange(nodes.ToArray());
            classNode.Expand();
        }

        /// <summary>
        /// Refreshes the class member detail view for the selected class member.
        /// </summary>
        /// <param name="tag">A ManagementClass, PropertyData or MethodData member object of the currently displayed ManagementClass.</param>
        private void RefreshClassMembersDetailView(object tag)
        {
            this.txtClassMemberDetail.Clear();

            ManagementClass c = this.CurrentClass;

            if (tag.GetType().IsAssignableFrom(typeof(ManagementClass)))
            {
                Font boldFont = new Font(this.txtClassMemberDetail.Font.FontFamily, this.txtClassMemberDetail.Font.Size, FontStyle.Bold);
                this.txtClassMemberDetail.Clear();
                this.txtClassMemberDetail.AppendText(@"public class ");
                this.txtClassMemberDetail.AppendText(c.Path.ClassName, boldFont);
                this.txtClassMemberDetail.AppendText("\r\n  Member of " + c.Path.NamespacePath + "\r\n\r\n");
                this.txtClassMemberDetail.AppendText("Summary:\r\n", boldFont);
                this.txtClassMemberDetail.AppendText(c.GetDescription() + "\r\n\r\n");
                this.txtClassMemberDetail.AppendText("Base Types:\r\n", boldFont);
                for (int i = 0; i < c.Derivation.Count; i++)
                {
                    this.txtClassMemberDetail.AppendText(new string(' ', i * 2));
                    this.txtClassMemberDetail.InsertLink(c.Derivation[i], c.Path.NamespacePath + ":" + c.Derivation[i]);
                    this.txtClassMemberDetail.AppendText("\r\n");
                }
            }

            else if (tag.GetType().IsAssignableFrom(typeof(PropertyData)))
            {
                // Display PropertyData properties
                PropertyData prop = (PropertyData)tag;

                Font boldFont = new Font(this.txtClassMemberDetail.Font.FontFamily, this.txtClassMemberDetail.Font.Size, FontStyle.Bold);
                string CimType = prop.GetRefType();

                this.txtClassMemberDetail.AppendText("public ");
                if (!String.IsNullOrEmpty(CimType))
                {
                    this.txtClassMemberDetail.InsertLink(CimType, c.Path.NamespacePath + ":" + CimType);
                }
                else
                {
                    this.txtClassMemberDetail.AppendText(prop.Type.ToString());
                }

                this.txtClassMemberDetail.AppendText((prop.IsArray ? "[]" : "") + " ");
                this.txtClassMemberDetail.AppendText(prop.Name + " ", boldFont);
                this.txtClassMemberDetail.AppendText("{ " + (prop.IsWritable() ? "set; " : "") + "get; }");
                this.txtClassMemberDetail.AppendText("\r\n  Member of " + c.Path.NamespacePath + ":" + c.Path.ClassName + "\r\n\r\n");
                if (!String.IsNullOrEmpty(prop.GetDescription()))
                {
                    this.txtClassMemberDetail.AppendText("Summary:\n", boldFont);
                    this.txtClassMemberDetail.AppendText(prop.GetDescription() + "\r\n\r\n");
                }

                var map = prop.GetValueMap();
                if (map.Count > 0)
                {
                    this.txtClassMemberDetail.AppendText("Values:\r\n", boldFont);
                    foreach (string mapping in map.Keys)
                    {
                        if (mapping == map[mapping])
                        {
                            this.txtClassMemberDetail.AppendText("    " + mapping + "\r\n");
                        }
                        else
                        {
                            this.txtClassMemberDetail.AppendText("    " + mapping + " - " + map[mapping] + "\r\n");
                        }
                    }
                    this.txtClassMemberDetail.AppendText("\r\n");
                }
                    
                if (prop.Origin != "" && prop.Origin != c.Path.ClassName)
                {
                    this.txtClassMemberDetail.AppendText("Origin:\n", boldFont);
                    this.txtClassMemberDetail.InsertLink(prop.Origin, c.Path.NamespacePath + ":" + prop.Origin);
                    this.txtClassMemberDetail.AppendText("\r\n\r\n");
                }
            }

            else if (tag.GetType().IsAssignableFrom(typeof(MethodData)))
            {
                // Display MethodData properties
                MethodData m = (MethodData)tag;
                PropertyData ret = m.GetReturnValueParameter();
                string mType = (ret != null) ? ret.Type.ToString() : "void";

                Font boldFont = new Font(this.txtClassMemberDetail.Font.FontFamily, this.txtClassMemberDetail.Font.Size, FontStyle.Bold);
                Font emFont = new Font(this.txtClassMemberDetail.Font.FontFamily, this.txtClassMemberDetail.Font.Size, FontStyle.Italic);

                this.txtClassMemberDetail.AppendText("public ");
                if (m.IsStatic())
                    this.txtClassMemberDetail.AppendText("static ");
                this.txtClassMemberDetail.AppendText(mType + " " + m.Name, boldFont);
                this.txtClassMemberDetail.AppendText("(");
                if (m.InParameters != null)
                {
                    int count = 0;
                    foreach (PropertyData prop in m.InParameters.Properties)
                    {
                        count++;
                        this.txtClassMemberDetail.AppendText(prop.Type.ToString(), boldFont);
                        this.txtClassMemberDetail.AppendText(" " + prop.Name);
                        if (count < m.InParameters.Properties.Count)
                            this.txtClassMemberDetail.AppendText(", ");
                    }
                }

                this.txtClassMemberDetail.AppendText(")\r\n  Member of " + c.Path.NamespacePath + ":" + c.Path.ClassName + "\r\n\r\n");

                // Show method origin if inherited
                if (m.Origin != "" && m.Origin != c.Path.ClassName)
                {
                    this.txtClassMemberDetail.AppendText("Origin:\n", boldFont);
                    this.txtClassMemberDetail.InsertLink(m.Origin, c.Path.NamespacePath + ":" + m.Origin);
                    this.txtClassMemberDetail.AppendText("\r\n\r\n");
                }

                // Show summary
                   
                if (!String.IsNullOrEmpty(m.GetDescription()))
                {
                    this.txtClassMemberDetail.AppendText("Summary:\n", boldFont);
                    this.txtClassMemberDetail.AppendText(m.GetDescription() + "\r\n\r\n");
                }

                // Show Parameters
                if (m.InParameters != null || m.OutParameters != null)
                {
                    this.txtClassMemberDetail.AppendText("Parameters:\n", boldFont);
                    if (m.InParameters != null)
                    {
                        foreach (PropertyData prop in m.InParameters.Properties)
                        {
                            this.txtClassMemberDetail.AppendText(prop.Name, emFont);
                            this.txtClassMemberDetail.AppendText(": " + prop.GetDescription() + "\r\n\r\n");
                        }
                    }

                    if (m.OutParameters != null)
                    {
                        foreach (PropertyData prop in m.OutParameters.Properties)
                        {
                            if (prop.Name != "ReturnValue")
                            {
                                this.txtClassMemberDetail.AppendText(prop.Name, emFont);
                                this.txtClassMemberDetail.AppendText(": " + prop.GetDescription() + "\r\n\r\n");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Refreshes the query tab for the specified ManagementClass.
        /// </summary>
        /// <param name="c">The System.Management.ManagementClass to be displayed.</param>
        /// <remarks>Cancels any currently running queries.</remarks>
        private void RefreshQueryView(ManagementClass c)
        {
            // Stop an existing query
            if (this.queryBroker != null)
            {
                this.queryBroker.Cancel();
            }

            var query = c.GetDefaultQuery();

            this.txtQuery.Text = query;
            this.queryBroker = new ManagementQueryBroker(query, this.CurrentNamespaceScope);

            InitQueryResultGrid(c);
        }

        /// <summary>
        /// Initializes the query results grid to receive results for the specified ManagementClass.
        /// </summary>
        /// <param name="c">The System.Management.ManagementClass to be displayed.</param>
        private void InitQueryResultGrid(ManagementClass c)
        {
            this.gridQueryResults.Rows.Clear();
            this.gridQueryResults.Columns.Clear();

            // Configure result context menu
            this.btnGetAssociatorsOf.Visible =
                this.btnGetReferencesOf.Visible =
                this.btnResultPropertiesSeparater.Visible =
                this.queryBroker == null || this.queryBroker.QueryType == WqlQueryType.Select;

            // Create count colIndex
            DataGridViewTextBoxColumn colIndex = new DataGridViewTextBoxColumn();
            colIndex.Name = colIndex.HeaderText = "#";
            colIndex.DefaultCellStyle.BackColor = SystemColors.ControlLight;
            colIndex.DefaultCellStyle.ForeColor = SystemColors.ControlDark;
            colIndex.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;

            // Create result inspector colIndex
            DataGridViewImageColumn colInspector = new DataGridViewImageColumn();
            colInspector.Image = this.ImageList1.Images["Property"];
            colInspector.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;

            this.gridQueryResults.Columns.AddRange(colIndex, colInspector);

            if (this.queryBroker == null || this.queryBroker.QueryType == WqlQueryType.Select)
            {
                // Add property columns
                foreach (PropertyData p in c.Properties)
                {
                    DataGridViewColumn colProperty;

                    // Create hyperlink columns for objects
                    if (p.Type == CimType.Object || p.Type == CimType.Reference)
                    {
                        colProperty = new DataGridViewLinkColumn();
                        DataGridViewLinkColumn link = (DataGridViewLinkColumn)colProperty;
                        link.LinkBehavior = LinkBehavior.HoverUnderline;
                        link.VisitedLinkColor = link.LinkColor;
                        link.ActiveLinkColor = link.LinkColor;
                    }

                    else
                    {
                        colProperty = new DataGridViewTextBoxColumn();
                    }

                    colProperty.Name = colProperty.HeaderText = p.Name;
                    if (p.IsKey())
                    {
                        colProperty.DefaultCellStyle.BackColor = SystemColors.Info;
                        colProperty.DefaultCellStyle.ForeColor = SystemColors.InfoText;
                    }

                    if (c.IsAssociation())
                        colProperty.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                    else
                        colProperty.AutoSizeMode = DataGridViewAutoSizeColumnMode.NotSet;

                    this.gridQueryResults.Columns.Add(colProperty);
                }
            }

            else
            {
                DataGridViewLinkColumn col = new DataGridViewLinkColumn();
                col.LinkBehavior = LinkBehavior.HoverUnderline;
                col.VisitedLinkColor = col.LinkColor;
                col.ActiveLinkColor = col.LinkColor;
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

                switch (this.queryBroker.QueryType)
                {
                    case WqlQueryType.AssociatorsOf:
                        col.Name = "Association";
                        break;

                    case WqlQueryType.ReferencesOf:
                        col.Name = "Reference";
                        break;
                }

                this.gridQueryResults.Columns.Add(col);
            }
        }

        /// <summary>
        /// Refreshes the code tab with script details for the specified ManagementClass.
        /// </summary>
        /// <param name="c">The System.Management.ManagementClass to be displayed.</param>
        private void RefreshScript(ManagementClass c)
        {
            if (this.CodeGenerator != null && c != null)
            {
                var query = c.GetDefaultQuery();

                // Reset script editor
                this.txtCode.IsReadOnly = false;
                this.txtCode.Text = String.Empty;
                this.txtCode.ConfigurationManager.Language = String.Empty;
                this.txtCode.ConfigurationManager.Configure();

                try
                {
                    this.txtCode.Text = this.CodeGenerator.GetScript(c, query);

                    // Update script
                    this.txtCode.ConfigurationManager.Language = this.CodeGenerator.Lexer;
                    this.txtCode.ConfigurationManager.Configure();

                    // Set colors
                    if (!String.IsNullOrEmpty(this.CodeGenerator.Lexer))
                    {
                        this.txtCode.Styles[this.txtCode.Lexing.StyleNameMap["STRING"]].ForeColor = Color.FromArgb(163, 21, 21);
                        this.txtCode.Styles[this.txtCode.Lexing.StyleNameMap["COMMENT"]].ForeColor = Color.FromArgb(0, 128, 0);
                        this.txtCode.Styles[this.txtCode.Lexing.StyleNameMap["LINENUMBER"]].ForeColor = Color.FromArgb(43, 145, 175);
                        this.txtCode.Styles[this.txtCode.Lexing.StyleNameMap["LINENUMBER"]].BackColor = SystemColors.Window;
                        this.txtCode.Styles[this.txtCode.Lexing.StyleNameMap["NUMBER"]].ForeColor = SystemColors.WindowText;
                        this.txtCode.Styles[this.txtCode.Lexing.StyleNameMap["OPERATOR"]].ForeColor = SystemColors.WindowText;

                        if (this.CodeGenerator.Lexer == "cs")
                        {
                            this.txtCode.Styles[this.txtCode.Lexing.StyleNameMap["GLOBALCLASS"]].ForeColor = Color.Blue;
                            this.txtCode.Styles[this.txtCode.Lexing.StyleNameMap["WORD2"]].ForeColor = Color.Blue;
                        }
                    }
                }

                catch (Exception e)
                {
                    this.txtCode.Text = e.Message;
                }

                this.txtCode.IsReadOnly = true;

                // Reset actions
                var toDelete = new List<ToolStripMenuItem>();
                foreach (ToolStripMenuItem item in this.menuStripCode.Items)
                {
                    if (item.Tag != null && item.Tag.GetType() == typeof(CodeGeneratorAction))
                        toDelete.Add(item);
                }

                foreach(ToolStripMenuItem item in toDelete)
                    this.menuStripCode.Items.Remove(item);

                // Update actions
                foreach (var action in this.CodeGenerator.GetActions(c, query))
                {
                    ToolStripMenuItem item = new ToolStripMenuItem
                    {
                        Text = action.Name,
                        Image = action.Image,
                        Tag = action,
                        Alignment = ToolStripItemAlignment.Right
                    };

                    item.Click += new EventHandler(OnActionClicked);

                    this.menuStripCode.Items.Add(item);
                }
            }
        }

        #endregion

        #region Queries

        /// <summary>
        /// Prepares the Query UI and executes a WQL query via a new ManagementQueryBroker.
        /// </summary>
        /// <param name="query">The WQL query to execute.</param>
        private void ExecuteQuery(String query)
        {
            // Reset UI
            this.ResetQueryResults();
            this.ToggleQueryUI(true);

            this.Log(LogLevel.Information, String.Format("Executing query: {0}", query));

            try
            {
                // Prepare the query broker
                this.queryBroker = new ManagementQueryBroker(this.txtQuery.Text, this.CurrentNamespaceScope);
                this.queryBroker.ObjectReady += new BrokerObjectReadyEventHandler(this.OnQueryResultReady);
                this.queryBroker.Completed += new BrokerCompletedEventHandler(this.OnQueryCompleted);

                // Init the grid view
                this.InitQueryResultGrid(this.queryBroker.ResultClass);

                // Fetch results
                this.queryBroker.ExecuteAsync();
            }

            catch (Exception e)
            {
                bool hasCode = false;
                UInt32 code = 0;

                if (e.GetType() == typeof(COMException))
                {
                    hasCode = true;
                    code = (UInt32) ((COMException)e).ErrorCode;
                }

                else if(e.GetType() == typeof(ManagementException))
                {
                    hasCode = true;
                    code = (UInt32) ((ManagementException)e).ErrorCode;
                }

                if (hasCode && Enum.IsDefined(typeof(ManagementError), code))
                {
                    var constant = ((ManagementError)code).ToString();
                    this.Log(LogLevel.Critical, String.Format("Query execution failed with {0:G} (0x{0:X}) {1}", code, constant));
                    
                    var description = ErrorCodes.ResourceManager.GetString(constant);
                    if (!String.IsNullOrEmpty(description))
                        this.Log(LogLevel.Information, description);
                }

                else
                {
                    this.Log(LogLevel.Critical, String.Format("Query execution failed with: {0}", e.Message));
                }

                // Generic exceptions may not trigger a 'Completed' event
                this.ToggleQueryUI(false);
            }
        }

        /// <summary>
        /// Handles a Completed event when a WMI query has completed.
        /// </summary>
        /// <param name="sender">The System.Management.ManagementQueryBroker that owns the completed query.</param>
        /// <param name="e">The BrokerCompletedEventArgs for this event.</param>
        private void OnQueryCompleted(object sender, BrokerCompletedEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new BrokerCompletedEventHandler(this.OnQueryCompleted), sender, e);
                return;
            }

            this.ToggleQueryUI(false);

            if (e.Success)
            {
                this.Log(LogLevel.Information, String.Format("Query completed in {0} with {1} results.",this.queryBroker.ExecutionTime, this.queryBroker.ResultCount));
            }

            else
            {
                var code = (UInt32)e.Status;
                if(Enum.IsDefined(typeof(ManagementError), code))
                {
                    var constant = ((ManagementError)code).ToString();
                    this.Log(LogLevel.Critical, String.Format("Query failed after {0} with {1:G} (0x{1:X}) {2}", this.queryBroker.ExecutionTime, code, constant));

                    var description = ErrorCodes.ResourceManager.GetString(constant);
                    if (!String.IsNullOrEmpty(description))
                        this.Log(LogLevel.Information, description);
                }

                else
                {
                    this.Log(LogLevel.Critical, String.Format("Query failed after {0} with {1:G} (0x{1:X}).", this.queryBroker.ExecutionTime, code));
                }
            }
        }

        /// <summary>
        /// Handles an ObjectReady event when a WMI object has been returned from a WMI query.
        /// </summary>
        /// <param name="sender">The System.Management.ManagementQueryBroker that returned this object.</param>
        /// <param name="e">The BrokerObjectReadyEventArgs for this event.</param>
        private void OnQueryResultReady(object sender, BrokerObjectReadyEventArgs e)
        {
            if (this.Disposing)
                return;

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new BrokerObjectReadyEventHandler(this.OnQueryResultReady), sender, e);
                return;
            }

            // Build an array of values
            var i = 0;
            var values = new String[e.NewObject.Properties.Count + 3];

            values[i++] = e.Index.ToString();
            values[i++] = null;

            if (WqlQueryType.Select == this.queryBroker.QueryType)
            {
                // Get the properties from the class, not the object as the returned
                // object may be a subclass with additional properies for which no
                // columns have been configured.
                foreach (PropertyData cProp in this.queryBroker.ResultClass.Properties)
                {
                    var oProp = e.NewObject.Properties[cProp.Name];
                    values[i++] = oProp.GetValueAsString(this.showMappedValues ? this.queryBroker.ResultClassValueMaps : null);
                }
            }

            else
            {
                values[i++] = e.NewObject.GetRelativePath();
            }

            this.gridQueryResults.Rows.Add(values);
        }

        /// <summary>
        /// Reset the query UI by removing previous results and resetting counters.
        /// </summary>
        private void ResetQueryResults()
        {
            this.gridQueryResults.Rows.Clear();
        }

        /// <summary>
        /// Toggles the query UI controls to feedback query status to the user.
        /// </summary>
        /// <param name="inProgress">Determines the state of UI components.</param>
        private void ToggleQueryUI(Boolean inProgress)
        {
            this.btnExecuteQuery.Visible = !inProgress;
            this.btnCancelQuery.Visible = inProgress;

            this.txtQuery.ReadOnly = inProgress;

            this.tabQuery.Cursor = inProgress ? Cursors.WaitCursor : Cursors.Default;
        }

        /// <summary>
        /// Shows the object inspector window for the specified ManagementBaseObject.
        /// </summary>
        /// <param name="managementObject">The ManagementBaseObject to be inspected.</param>
        /// <param name="location">The location (relative to the screen) to display the window.</param>
        private void ShowInspector(ManagementBaseObject managementObject, Point location)
        {
            this.gridQueryResults.Cursor = Cursors.WaitCursor;

            // Create form
            ManagementObjectInspectorForm popup = new ManagementObjectInspectorForm();
            popup.ShowMappedValues = this.showMappedValues;
            popup.Scope = this.CurrentNamespaceScope;
            popup.ManagementClass = this.queryBroker.ResultClass;
            popup.ManagementObject = managementObject;

            // Offset location to screen bounds
            Rectangle bounds = Screen.FromControl(this).WorkingArea;
            location.X = Math.Min(location.X, bounds.Width - popup.Width);
            location.Y = Math.Min(location.Y, bounds.Height - popup.Height);
            popup.Location = location;

            // Show
            this.gridQueryResults.Cursor = Cursors.Default;
            popup.Show(this);
        }

        private void ShowInspectorForLinkedObject(DataGridViewCellEventArgs e)
        {
            Boolean selectMode = this.queryBroker.QueryType == WqlQueryType.Select;

            // A hyperlinked object was click. Get c object
            ManagementBaseObject mObject = null;
            if (selectMode)
            {
                string propertyName = this.gridQueryResults.Columns[e.ColumnIndex].HeaderText;
                if (this.queryBroker.ResultClass.Properties[propertyName].Type == CimType.Reference)
                {
                    // Link was a reference to an instance. Fetch the instance
                    string objectPath = this.queryBroker.Results[e.RowIndex].Properties[propertyName].Value.ToString();

                    var scope = new ManagementScope(this.CurrentNamespacePath, this.conOpts);
                    mObject = new ManagementObject(scope, new ManagementPath(objectPath), new ObjectGetOptions());
                }

                else
                {
                    // Link was to an object instance
                    mObject = (ManagementBaseObject) this.queryBroker.Results[e.RowIndex].Properties[propertyName].Value;
                }
            }

            else
            {
                // Select the whole object for 'Assoc' and 'Ref' queries
                mObject = this.queryBroker.Results[e.RowIndex];
            }

            // Get cell location
            Rectangle cell = this.gridQueryResults.GetCellDisplayRectangle(selectMode ? e.ColumnIndex : 1, e.RowIndex, true);
            Point location = this.gridQueryResults.PointToScreen(new Point(cell.Right, cell.Bottom));

            // Show object inspector
            this.ShowInspector(mObject, location);
        }

        #endregion

        #region Code generation

        /// <summary>
        /// Initializes the Scintilla.Net code editor control.
        /// </summary>
        /// <remarks>The control is not added in the designer as it is unable to resolve the unmanaged DLLs</remarks>
        private void InitCodeEditor()
        {
            this.txtCode = new Scintilla
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9.75f)
            };

            // Show line numbers
            this.txtCode.Margins[0].Width = 40;
            this.txtCode.Scrolling.ScrollBars = ScrollBars.Both;

            // Insert as first control so it isn't overlapped
            this.tabCode.Controls.Add(this.txtCode);
            this.tabCode.Controls.SetChildIndex(this.txtCode, 0);
        }

        /// <summary>
        /// Refreshes the code language menu with menu items for all available code generators.
        /// </summary>
        private void RefreshCodeGeneratorMenu()
        {
            var generators = CodeGeneratorFactory.CodeGenerators;

            foreach (var generator in generators)
            {
                // Search for an existing language menu
                ToolStripMenuItem mnuLang = null;
                foreach (ToolStripMenuItem langItem in this.mnuScriptTemplates.DropDownItems)
                {
                    if (langItem.Text.Equals(generator.Language))
                    {
                        mnuLang = langItem;
                        break;
                    }
                }

                // Create a missing language menu
                if (mnuLang == null)
                {
                    mnuLang =(ToolStripMenuItem) mnuScriptTemplates.DropDownItems.Add(generator.Language);
                }

                // Create script menu item
                var scriptItem = new ToolStripMenuItem
                {
                    Text = generator.Name,
                    Tag = generator
                };
                scriptItem.Click += new EventHandler(OnScriptMenuItemClick);
                mnuLang.DropDownItems.Add(scriptItem);
            }

            // Sort menus
            this.mnuScriptTemplates.DropDownItems.Sort();
            foreach (ToolStripMenuItem child in this.mnuScriptTemplates.DropDownItems)
            {
                child.DropDownItems.Sort();
            }
        }

        /// <summary>
        /// Saves the contents of the code editor control to a script file.
        /// </summary>
        /// <param name="path">File path to save to.</param>
        private void SaveScript(string path)
        {
            System.IO.StreamWriter stream = new System.IO.StreamWriter(path, false);

            try
            {
                stream.Write(this.txtCode.Text);
            }
            finally
            {
                stream.Close();
            }
        }

        /// <summary>
        /// Displays a file save dialog to save the contents of the code editor control to a script file.
        /// </summary>
        private void SaveScript()
        {
            if (this.CodeGenerator == null) return;

            SaveFileDialog dlg = new SaveFileDialog();
            dlg.FileName = this.CodeGenerator.Name + "." + this.CodeGenerator.FileExtension;
            dlg.OverwritePrompt = true;
            dlg.Filter = this.CodeGenerator.Language + " Scripts|*." + this.CodeGenerator.FileExtension + "|All Files|*.*";
            dlg.CheckPathExists = true;
            dlg.Tag = "Save Script";

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                this.SaveScript(dlg.FileName);
            }
        }

        #endregion

        #region Logging

        /// <summary>
        /// Logs a message to the log window.
        /// </summary>
        /// <param name="level">The log entry type.</param>
        /// <param name="message">The message to be logged.</param>
        private void Log(LogLevel level, String message)
        {
            var item = new ListViewItem(message);
            item.ImageKey = level.ToString();

            this.listViewLog.Items.Add(item);
            item.EnsureVisible();
        }

        #endregion

        #region UI Event Handlers

        private void splitContainer1_SplitterMoved(object sender, SplitterEventArgs e)
        {
            resetClassColumnHeader();
        }

        private void resetClassColumnHeader()
        {
            listViewClasses.Columns[0].Width = listViewClasses.ClientRectangle.Width;
        }

        private void treeViewNamespaces_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (this.updatingNavigation)
                return;

            this.Cursor = Cursors.WaitCursor;
            this.CurrentNamespacePath = (String)e.Node.Tag;
            this.Cursor = Cursors.Default;
        }

        private void listViewClasses_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.updatingNavigation || this.listViewClasses.SelectedItems.Count != 1)
                return;

            this.Cursor = Cursors.WaitCursor;
            this.CurrentClassPath = (String) this.listViewClasses.SelectedItems[0].Tag;
            this.Cursor = Cursors.Default;
        }

        private void treeViewClassMembers_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (this.updatingNavigation)
                return;

            if (null != treeViewClassMembers.SelectedNode && null != treeViewClassMembers.SelectedNode.Tag)
            {
                this.Cursor = Cursors.WaitCursor;
                this.RefreshClassMembersDetailView(treeViewClassMembers.SelectedNode.Tag);
                this.Cursor = Cursors.Default;
            }
        }

        private void txtClassMemberDetail_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            // RTFex uses # to separate the link text from its href.
            string[] link = e.LinkText.Split('#');
            string target = link[link.Length - 1];
            this.CurrentClassPath = target;
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void executeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.ExecuteQuery(this.txtQuery.Text);
        }

        private void btnCancelQuery_Click(object sender, EventArgs e)
        {
            this.queryBroker.Cancel();
        }

        private void frmMain_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyValue == 116)
                this.ExecuteQuery(this.txtQuery.Text);
        }

        private void txtClassFilter_KeyUp(object sender, KeyEventArgs e)
        {
            this.RefreshClassListFilter();
        }

        private void toolStripMenuItem1_Click(object sender, EventArgs e)
        {
            this.RefreshClassListFilter();
        }

        private void gridQueryResults_CellMouseEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex == -1 || e.ColumnIndex == -1) return;

            if (this.gridQueryResults.Rows[e.RowIndex].Cells[e.ColumnIndex].GetType() == typeof(DataGridViewImageCell))
                this.gridQueryResults.Cursor = Cursors.Hand;
            else
                this.gridQueryResults.Cursor = Cursors.Default;
        }

        private void gridQueryResults_CellClicked(object sender, DataGridViewCellEventArgs e)
        {
            Type cellType = this.gridQueryResults.Rows[e.RowIndex].Cells[e.ColumnIndex].GetType();

            // Handle 'properties' button clicks
            if (e.ColumnIndex == 1)
            {
                this.gridQueryResults_CellDoubleClicked(sender, e);
            }
        }

        private void gridQueryResults_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            Type cellType = this.gridQueryResults.Rows[e.RowIndex].Cells[e.ColumnIndex].GetType();

            // Handle hyperlink clicks
            if (cellType == typeof(DataGridViewLinkCell))
            {
                ShowInspectorForLinkedObject(e);
            }
        }

        private void gridQueryResults_CellDoubleClicked(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            // Highlight row
            this.gridQueryResults.ClearSelection();
            this.gridQueryResults.Rows[e.RowIndex].Selected = true;

            // Show inspector in place
            Rectangle cell = this.gridQueryResults.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, true);
            Point location = this.gridQueryResults.PointToScreen(new Point(cell.Right, cell.Bottom));

            this.ShowInspector(this.queryBroker.Results[e.RowIndex], location);
        }

        private void gridQueryResults_CellMouseUp(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                if (e.RowIndex >= 0 && e.RowIndex >= 0)
                {
                    // Select a row on right click
                    this.gridQueryResults.Rows[e.RowIndex].Selected = true;

                    // Get bounds for result context menu
                    var loc = this.gridQueryResults.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, true);
                    loc.X += e.X;
                    loc.Y += e.Y;

                    // Show context menu
                    this.menuQueryRow.Show(this.gridQueryResults.PointToScreen(loc.Location));
                }
            }
        }

        private void btnResultProperies_Click(object sender, EventArgs e)
        {
            if (this.gridQueryResults.SelectedRows.Count == 1)
            {
                // Find location of top right of the 'property' button for the selected row
                var index = this.gridQueryResults.SelectedRows[0].Index;
                var rect = this.gridQueryResults.GetCellDisplayRectangle(1, index, true);
                var loc = this.gridQueryResults.PointToScreen(new Point(rect.Right, rect.Top));

                // Display object inspector
                this.ShowInspector(this.queryBroker.Results[index], loc);
            }
        }

        private void btnGetAssociatorsOf_Click(object sender, EventArgs e)
        {
            if (this.gridQueryResults.SelectedRows.Count == 1)
            {
                // Execute 'Associators Of' query for selected result
                var obj = this.queryBroker.Results[this.gridQueryResults.SelectedRows[0].Index];
                var query = String.Format("ASSOCIATORS OF {{{0}}}", obj.GetRelativePath());
                this.txtQuery.Text = query;
                this.ExecuteQuery(query);
            }
        }

        private void btnGetReferencesOf_Click(object sender, EventArgs e)
        {
            if (this.gridQueryResults.SelectedRows.Count == 1)
            {
                // Execute 'Associators Of' query for selected result
                var obj = this.queryBroker.Results[this.gridQueryResults.SelectedRows[0].Index];
                var query = String.Format("REFERENCES OF {{{0}}}", obj.GetRelativePath());
                this.txtQuery.Text = query;
                this.ExecuteQuery(query);
            }
        }

        private void OnScriptMenuItemClick(object sender, EventArgs e)
        {
            this.CodeGenerator = (ICodeGenerator)((ToolStripMenuItem)sender).Tag;
        }

        private void btnSaveScript_Click(object sender, EventArgs e)
        {
            SaveScript();
        }

        private void OnActionClicked(object sender, EventArgs e)
        {
            var action = ((ToolStripMenuItem)sender).Tag as CodeGeneratorAction;
            if (action == null || this.CodeGenerator == null || this.CurrentClass == null)
                return;

            this.CodeGenerator.ExecuteAction(action, this.CurrentClass, this.CurrentClass.GetDefaultQuery());
        }

        private void showSystemClassesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.showSystemClassesToolStripMenuItem.Checked = !this.showSystemClassesToolStripMenuItem.Checked;
            this.showSystemClasses = this.showSystemClassesToolStripMenuItem.Checked;
            this.RefreshClassListFilter();
        }

        private void showmappedValuesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.showmappedValuesToolStripMenuItem.Checked = !this.showmappedValuesToolStripMenuItem.Checked;
            this.showMappedValues = this.showmappedValuesToolStripMenuItem.Checked;
        }

        #endregion
    }

    internal enum LogLevel
    {
        Information,
        Warning,
        Critical
    }
}
