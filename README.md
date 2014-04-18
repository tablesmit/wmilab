## WMI Lab

### Synopsis
WMI Lab expands on common WMI tools to allow for inspection, querying, deeper interrogation and code generation of WMI classes on local or remote Windows systems.

### Contributing

__Requirements:__

* Microsoft .Net Framework 3.5
* Microsoft Visual Studio 2010

### Bugs

* Line ending ignored in member descriptors
* Query must be cancelled before changing to another class

### Todo

* Add error feedback for bad queries
* Add support for remote connections
* Add support for watching event queries
  Eg: SELECT * FROM __InstanceModificationEvent WHERE TargetInstance ISA 'Win32_Process'
* Add code generators
* Add progress indicator for namspace/class lists