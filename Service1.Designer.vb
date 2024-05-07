Imports System.Reflection
Imports System.ServiceProcess

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class Service1
    Inherits System.ServiceProcess.ServiceBase

    'UserService overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private Shared Sub RunInteractive(ByVal servicesToRun As ServiceBase())

        Console.WriteLine("Services running in interactive mode.")
        Console.WriteLine()
        Dim onStartMethod As MethodInfo = GetType(ServiceBase).GetMethod("OnStart", BindingFlags.Instance Or BindingFlags.NonPublic)

        For Each service As ServiceBase In servicesToRun
            Console.Write("Starting {0}...", service.ServiceName)
            onStartMethod.Invoke(service, New Object() {New String() {}})
            Console.Write("Started")
        Next

        Console.WriteLine()
        Console.WriteLine()
        Console.WriteLine("Press any key to stop the services and end the process...")
        Console.ReadKey()
        Console.WriteLine()
        Dim onStopMethod As MethodInfo = GetType(ServiceBase).GetMethod("OnStop", BindingFlags.Instance Or BindingFlags.NonPublic)

        For Each service As ServiceBase In servicesToRun
            Console.Write("Stopping {0}...", service.ServiceName)
            onStopMethod.Invoke(service, Nothing)
            Console.WriteLine("Stopped")
        Next

        Console.WriteLine("All services stopped.")
        System.Threading.Thread.Sleep(1000)
    End Sub

    ' The main entry point for the process
    <MTAThread()> _
    <System.Diagnostics.DebuggerNonUserCode()> _
    Shared Sub Main()
        Dim ServicesToRun() As System.ServiceProcess.ServiceBase

        ' More than one NT Service may run within the same process. To add
        ' another service to this process, change the following line to
        ' create a second service object. For example,
        '
        '   ServicesToRun = New System.ServiceProcess.ServiceBase () {New Service1, New MySecondUserService}
        '
        ServicesToRun = New System.ServiceProcess.ServiceBase() {New Service1}

        If Environment.UserInteractive Then
            RunInteractive(ServicesToRun)
        Else
            System.ServiceProcess.ServiceBase.Run(ServicesToRun)
        End If
    End Sub

    'Required by the Component Designer
    Private components As System.ComponentModel.IContainer

    ' NOTE: The following procedure is required by the Component Designer
    ' It can be modified using the Component Designer.  
    ' Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()> _
    Private Sub InitializeComponent()
        components = New System.ComponentModel.Container()
        Me.ServiceName = "Service1"
    End Sub

End Class
