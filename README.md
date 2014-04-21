## WMI Lab
### Synopsis
WMI Lab expands on common WMI tools to allow for inspection, querying, deeper interrogation and code generation of WMI classes on local or remote Windows systems.

### Contributing
__Requirements:__

* Microsoft Visual Studio 2010

### Bugs
* Line ending ignored in member descriptors
* Class view flickers when filtering the class list
* Queries which return subclasses fail to order columns (eg. CIM_SYSTEM)
* Value maps are not loaded for subclass results of a base class query 
  eg. the Win32_ComputerSystem result in a CIM_System query
  
### Todo
* Add support for remote connections
* Add progress indicator for namespace/class lists
* Add state restoration for UI controls and navigation
* Add common text functions to query results, inspector, log, detail view, etc.

* Add about dialog
* Add license details
* Add license details for Scintilla.Net
* Add attribution header to all files
* Improve code documentation and naming
* Add WiX Installer project

### Example Queries

* Grouped Event query

  `SELECT * FROM __InstanceModificationEvent WITHIN 5 WHERE TargetInstance ISA 'Win32_Process' GROUP WITHIN 5`
  
  