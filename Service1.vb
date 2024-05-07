Imports Pandorian.Engine
Imports Un4seen.Bass
Imports Microsoft.VisualBasic.Logging
Imports System.IO
Imports System.Net
Imports System.Runtime.Serialization.Formatters.Binary
Imports System.Runtime.InteropServices
Imports System.Security.Cryptography
Imports Un4seen.Bass.AddOn
Imports System.Runtime.CompilerServices
Imports System.ComponentModel
Imports System.Threading
Imports System.Net.Sockets
Imports System.Text
Imports Newtonsoft.Json

'THIS APP 64-BIT ONLY, INSTALL WITH c:\Windows\Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe
Public Class Service1
    Dim TCPPort As Integer = 1561
    Dim Pandora As API
    Dim Proxy As Net.WebProxy
    Dim BASSReady As Boolean = False
    Dim ProxyPtr As IntPtr
    Dim AAC As Integer
    Dim Stream As Integer
    Dim DSP As Misc.DSP_Gain = New Misc.DSP_Gain()
    Dim Sync As SYNCPROC = New SYNCPROC(AddressOf SongEnded)
    'Dim ResumePlaying As Boolean = True
    Dim Stations As New SortedDictionary(Of String, String)

    Protected Overrides Sub OnStart(ByVal args() As String)
        ' Add code here to start your service. This method should set things
        ' in motion so your service can do its work.
        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory)

        If My.Settings.pandoraUsername = "" Or My.Settings.pandoraPassword = "" Then
            MakeLogEntry("Pandora credentials not supplied in App.config.  Exiting.")
            [Stop]()
            Exit Sub
        End If

        Process.Start("wmic", "process where name=""TandoraTCP.exe"" CALL setpriority 128")

        Try

            Dim thdListener As Thread = New Thread(AddressOf ListenForClients) With {.IsBackground = True}
            thdListener.Start()

        Catch ex As Exception
            MakeLogEntry("Exception Message:" & vbCrLf & ex.Message & vbCrLf & vbCrLf & "Exception Trace:" & vbCrLf & ex.StackTrace)
            [Stop]()
            Exit Sub
        End Try

        My.Settings.launchCount = My.Settings.launchCount + 1
        My.Settings.Save()

        Execute(Sub() RunNow())

        Bass.BASS_ChannelPause(Stream)

    End Sub

    Sub ListenForClients()
        Dim tcpListener = New TcpListener(IPAddress.Any, TCPPort)

        tcpListener.Start()

        MakeLogEntry("TandoraTCP is now listening for clients on port " & TCPPort)

        While True
            'blocks until a client has connected to the server
            Dim client As TcpClient = tcpListener.AcceptTcpClient
            'create a thread to handle communication with connected client
            Dim clientThread As Thread = New Thread(New ParameterizedThreadStart(AddressOf HandleClientComm))
            clientThread.Start(client)
        End While

    End Sub

    Sub HandleClientComm(ByVal client As Object)

        Try

            Dim tcpClient As TcpClient = CType(client, TcpClient)
            Dim clientStream As NetworkStream = tcpClient.GetStream

            If clientStream.CanRead Then
                Dim bytesRead As Integer = 0
                Dim bytes(tcpClient.ReceiveBufferSize) As Byte
                bytesRead = clientStream.Read(bytes, 0, CInt(tcpClient.ReceiveBufferSize))

                ReDim Preserve bytes(bytesRead - 1) 'trim to actual data size, removes a bunch of empties and line breaks

                Dim ReceivedCommand As String = Encoding.ASCII.GetString(bytes).Trim.ToLower

                MakeLogEntry("Command '" & ReceivedCommand & "' received")

                Dim Response As String = ""

                If ReceivedCommand.StartsWith("change station:") Then
                    Bass.BASS_ChannelStop(Stream)

                    For Each s In Pandora.AvailableStations
                        If s.Id = ReceivedCommand.Substring(15) Then
                            Pandora.CurrentStation = s
                            Exit For
                        End If
                    Next

                    My.Settings.lastStationID = Pandora.CurrentStation.Id
                    My.Settings.Save()

                    MakeLogEntry("Station changed to: " + Pandora.CurrentStation.Name)

                    SeeIfLastSongNeedsToBeReplayed()

                    Execute(Sub() PlayCurrentSong())

                    Response = JsonConvert.SerializeObject(GetStatus())

                ElseIf ReceivedCommand = "playpause" Then
                    If BASSChannelState() = BASSActive.BASS_ACTIVE_PLAYING Then
                        Bass.BASS_ChannelPause(Stream)
                    ElseIf BASSChannelState() = BASSActive.BASS_ACTIVE_PAUSED Then
                        Bass.BASS_ChannelPlay(Stream, False)
                    ElseIf BASSChannelState() = BASSActive.BASS_ACTIVE_STOPPED Then 'And ResumePlaying = False Then
                        'ResumePlaying = True
                        Bass.BASS_ChannelPlay(Stream, False)
                    End If

                    Response = JsonConvert.SerializeObject(GetStatus())

                ElseIf ReceivedCommand = "next" Then
                    If Pandora.CanSkip(Pandora.CurrentStation) Then Execute(Sub() PlayNextSong(True))
                    Response = JsonConvert.SerializeObject(GetStatus())

                    'ElseIf ReceivedCommand = "getstationlist" Then
                    '  LoadStationList()
                    '  Response = JsonConvert.SerializeObject(Stations)

                ElseIf ReceivedCommand = "update" Then
                    Response = JsonConvert.SerializeObject(GetStatus())

                Else
                    Response = "Unkown Command!"
                End If

                If clientStream.CanWrite Then
                    MakeLogEntry(Response)
                    Dim sendBytes As [Byte]() = Encoding.UTF8.GetBytes(Response)
                    clientStream.Write(sendBytes, 0, sendBytes.Length)
                End If

            End If

            tcpClient.Close()

        Catch ex As Exception
            EventLog.WriteEntry("TCPToTelegram", ex.Message & vbCrLf & vbCrLf & ex.StackTrace, EventLogEntryType.Error)
        End Try

    End Sub

    Function GetStatus() As Dictionary(Of String, Object)
        Dim dictStatus As New Dictionary(Of String, Object) From {
            {"IsPlaying", BASSChannelState() = BASSActive.BASS_ACTIVE_PLAYING},
            {"BASSState", BASSChannelState().ToString},
            {"CurrentStation", Pandora.CurrentStation.Id},
            {"CurrentSong", Pandora.CurrentStation.CurrentSong.Title},
            {"CurrentArtist", Pandora.CurrentStation.CurrentSong.Artist},
            {"SongElapsed", Math.Round(CurrentPositionSecs(), 0)},
            {"SongDuration", Math.Round(SongDurationSecs(), 0)}, '{"AlbumDetailsURL", Pandora.CurrentStation.CurrentSong.AlbumDetailsURL.Replace("http://", "https://")},
            {"AlbumArtURL", Pandora.CurrentStation.CurrentSong.AlbumArtLargeURL.Replace("http://", "https://").Replace("1080W_1080H", "250W_250H")},
            {"StationList", Stations}
        }

        Return dictStatus
    End Function

    Function BASSChannelState() As BASSActive
        'sometimes when changing channels BASS goes into stalled state. presumably waiting for next song to begin?
        'this gives it a moment to start before we send a status back to the client
        If Bass.BASS_ChannelIsActive(Stream) = BASSActive.BASS_ACTIVE_STALLED Then Thread.Sleep(1500)
        Return Bass.BASS_ChannelIsActive(Stream)
    End Function
    Sub RunNow()
        RestorePandoraObject()

        If Not My.Settings.noProxy Then
            Me.Proxy = New Net.WebProxy(My.Settings.proxyAddress)
            If Not String.IsNullOrEmpty(My.Settings.proyxUsername) And Not String.IsNullOrEmpty(My.Settings.proxyPassword) Then
                Me.Proxy.Credentials = New Net.NetworkCredential(My.Settings.proyxUsername, My.Settings.proxyPassword)
            End If
            Pandora.Proxy = Me.Proxy
        Else
            Me.Proxy = Nothing
            Pandora.Proxy = Nothing
        End If

        WaitForNetConnection()

        If Not IsLoggedIn() Then LoginToPandora()

        LoadStationList()

        If Not IsNothing(Pandora.CurrentStation) Then

            If Not String.IsNullOrEmpty(Pandora.CurrentStation.Id) Then

                MakeLogEntry("Current station: " + Pandora.CurrentStation.Name)

                InitBass()

                SeeIfLastSongNeedsToBeReplayed()

                Execute(Sub() PlayCurrentSong())

            End If
        End If
    End Sub

    Sub SongEnded(ByVal handle As Integer, ByVal channel As Integer, ByVal data As Integer, ByVal user As IntPtr)
        Execute(Sub() PlayNextSong(False))
    End Sub

    Sub PlayNextSong(Skip As Boolean)

        Bass.BASS_ChannelStop(Stream)
        Bass.BASS_StreamFree(Stream)
        Pandora.CurrentStation.GetNextSong(Skip, Pandora.SkipHistory)
        'ResumePlaying = True
        PlayCurrentSong() 'no need to use executedelegate as parent uses delegate
    End Sub
    Sub PlayCurrentSong() ' THIS SHOULD ONLY HAVE 4 REFERENCES (PlayNextSong/RunNow/ReplaySong/PowerModeChanged)

        Dim Song As New Data.PandoraSong

        If IsNothing(Pandora.CurrentStation.CurrentSong) Then
            Song = Pandora.CurrentStation.GetNextSong(False, Nothing)
        Else
            Song = Pandora.CurrentStation.CurrentSong
        End If

        If Pandora.CurrentStation.SongLoadingOccurred Then
            MakeLogEntry(">>>GOT NEW SONGS FROM PANDORA<<<")
        End If

        PlayCurrentSongWithBASS()

        'If Pandora.CurrentStation.CurrentSong.AudioDurationSecs < 60 Then
        '    lblSongName.Text = "This is a 42 sec blank audio track"
        '    lblArtistName.Text = "Pandora is punishing you for excessive skipping :-("
        '    lblAlbumName.Text = "This will correct itself in about 24hrs"
        '    SongCoverImage.Image = Nothing
        '    btnLike.Enabled = False
        '    btnDislike.Enabled = False
        '    btnPlayPause.Enabled = False
        '    btnSkip.Enabled = False
        '    btnBlock.Enabled = False
        'Else
        '    lblSongName.Text = Song.Title
        '    lblArtistName.Text = Song.Artist
        '    lblAlbumName.Text = Song.Album
        'End If

        'RaiseEvent SongInfoUpdated(lblSongName.Text, lblArtistName.Text, lblAlbumName.Text)

        SavePandoraObject()

        'tbLog.AppendText(Pandora.SkipHistory.PrintGlobalSkipCount() + vbCrLf)
        'tbLog.AppendText(Pandora.SkipHistory.PrintStationSkipCount(Pandora.CurrentStation) + vbCrLf)
        'tbLog.AppendText("------------------------------------------------------------------------------------------" + vbCrLf)

    End Sub

    Private Sub PlayCurrentSongWithBASS()
        If Not Stream = 0 Then
            Bass.BASS_ChannelStop(Stream)
            Stream = 0
        End If

        Stream = Bass.BASS_StreamCreateURL(
                Pandora.CurrentStation.CurrentSong.AudioUrlMap(My.Settings.audioQuality).Url,
                0,
                BASSFlag.BASS_STREAM_AUTOFREE,
                Nothing,
                IntPtr.Zero)

        If Not Stream = 0 Then
            MakeLogEntry("Playing the song now: " & Pandora.CurrentStation.CurrentSong.Title & " by " & Pandora.CurrentStation.CurrentSong.Artist)
            Bass.BASS_ChannelSetSync(Stream, BASSSync.BASS_SYNC_END, 0, Sync, IntPtr.Zero)
            'Bass.BASS_ChannelSetAttribute(Stream, BASSAttribute.BASS_ATTRIB_VOL, volSlider.Value / 100)

            DSP.ChannelHandle = Stream
            DSP.Gain_dBV = Pandora.CurrentStation.CurrentSong.TrackGain
            DSP.Start()
            MakeLogEntry("ReplayGain Applied: " + Pandora.CurrentStation.CurrentSong.TrackGain.ToString + " dB")

            'If ResumePlaying Then
            Bass.BASS_ChannelPlay(Stream, False)
            'End If

            Pandora.CurrentStation.CurrentSong.PlayingStartTime = Now
            Pandora.CurrentStation.CurrentSong.AudioDurationSecs = SongDurationSecs()

        Else
            If Bass.BASS_ErrorGetCode = BASSError.BASS_ERROR_FILEOPEN Then
                Throw New PandoraException(ErrorCodeEnum.SONG_URL_NOT_VALID, "Audio URL has probably expired...")
            Else
                MakeLogEntry("Couldn't open stream: " + Bass.BASS_ErrorGetCode().ToString + ". Going to try logging into Pandora again.")
                'Execute(Sub() PlayNextSong(False))
                ReLoginToPandora()
            End If
        End If

    End Sub

    Function CurrentPositionSecs() As Double
        Dim pos As Long = Bass.BASS_ChannelGetPosition(Stream)
        Return Bass.BASS_ChannelBytes2Seconds(Stream, pos)
    End Function
    Function SongDurationSecs() As Double
        Dim len As Long = Bass.BASS_ChannelGetLength(Stream)
        Return Bass.BASS_ChannelBytes2Seconds(Stream, len)
    End Function

    Private ErrCount As Integer = 0
    Private Delegate Sub ExecuteDelegate()
    Private Sub Execute(Logic As ExecuteDelegate)
        If ErrCount > 0 Then
            ErrCount = 0
            Exit Sub
        End If

        Try
            Logic()
        Catch pex As PandoraException

            Select Case pex.ErrorCode
                Case ErrorCodeEnum.AUTH_INVALID_TOKEN
                    Try
                        MakeLogEntry("Session expired. Loggin in again...")
                        ReLoginToPandora()
                    Catch ex As Exception
                        MakeLogEntry("Pandora session has expired. Tried to re-login but something went wrong :-( Try restarting TandoraTCP...")
                        'AfterErrorActions()
                    End Try
                Case ErrorCodeEnum.SONG_URL_NOT_VALID
                    MakeLogEntry("Song URL expired. Will fetch new songs...")
                    Pandora.CurrentStation.CurrentSong = Nothing
                    Pandora.CurrentStation.PlayList.Clear()
                    Execute(Logic)
                Case ErrorCodeEnum.LICENSE_RESTRICTION
                    MakeLogEntry("Looks like your country is not supported. Try using a proxy...")
                    AfterErrorActions()
                Case Else
                    AfterErrorActions()
                    ErrCount = 1
            End Select

        Catch ex As Exception
            AfterErrorActions()
        End Try
    End Sub

    Sub AfterErrorActions()
        [Stop]()
        Exit Sub
    End Sub
    Private Sub SavePandoraObject()
        If Not IsNothing(Pandora) Then
            Using stream As Stream = File.Create("api.dat")
                Try
                    Dim formatter As New BinaryFormatter()
                    formatter.Serialize(stream, Pandora)
                    MakeLogEntry("Saved the api object to disk...")
                Catch e As Exception
                    MakeLogEntry("Failed to save the api object to disk...")
                End Try
            End Using
        End If
    End Sub
    Private Sub ReLoginToPandora()
        Pandora.ClearSession(My.Settings.pandoraOne)
        SavePandoraObject()
        Execute(Sub() RunNow())
    End Sub
    Private Sub SeeIfLastSongNeedsToBeReplayed()

        If Not IsNothing(Pandora.CurrentStation.CurrentSong) Then
            If Pandora.CurrentStation.CurrentSong.DurationElapsed Then
                Pandora.CurrentStation.CurrentSong = Nothing
                MakeLogEntry("No need to replay the last song...")
            Else
                MakeLogEntry("Has to replay the last song...")
            End If
        End If

    End Sub
    Sub InitBass()
        If Not BASSReady Then

            BassNet.Registration("pandorian@sharklasers.com", "2X2531425283122")
            BASSReady = Bass.BASS_Init(-1, 44100, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero)

            Dim sw As New Stopwatch
            sw.Start()
            Do Until BASSReady
                If sw.ElapsedMilliseconds > 10000 Then
                    Exit Do
                End If
                System.Threading.Thread.Sleep(1000)
            Loop
            sw.Stop()

            If Not BASSReady Then
                MakeLogEntry("Sorry, having trouble accessing your audio device :-( Please double check your gear and restart TandoraTCP...")
            End If

            If Not My.Settings.noProxy Then
                Dim proxy As String = My.Settings.proyxUsername + ":" +
                                      My.Settings.proxyPassword + "@" +
                                      My.Settings.proxyAddress.Replace("http://", "")
                ProxyPtr = Marshal.StringToHGlobalAnsi(proxy)
                Bass.BASS_SetConfigPtr(BASSConfig.BASS_CONFIG_NET_PROXY, ProxyPtr)
            End If
            AAC = Bass.BASS_PluginLoad("bass_aac.dll")
            MakeLogEntry("Initialized BASS...")
        End If
    End Sub

    Sub DeInitBass()
        If BASSReady Then
            Bass.BASS_ChannelStop(Stream)
            Bass.BASS_StreamFree(Stream)
            Bass.BASS_PluginFree(AAC)
            Bass.BASS_Free()
            DSP.Stop()
            Marshal.FreeHGlobal(ProxyPtr)
            BASSReady = False
            MakeLogEntry("De-Initialized BASS...")
        End If
    End Sub
    Sub LoadStationList()
        If Not Pandora.AvailableStations.Count = 0 Then
            Dim FoundLastPlayedStation As Boolean

            Stations.Clear()

            For Each Station In Pandora.AvailableStations
                Stations.Add(Station.Name, Station.Id)
                If My.Settings.lastStationID = Station.Id Then
                    Pandora.CurrentStation = Station
                    FoundLastPlayedStation = True
                End If
            Next

            If Not FoundLastPlayedStation Then
                Pandora.CurrentStation = Pandora.AvailableStations(0)
            End If

            MakeLogEntry("Loaded the stations list...")
        Else
            MakeLogEntry("Sorry, no stations were found in your a/c. Please visit pandora.com and create some stations.")
        End If
    End Sub
    Public Function IsLoggedIn() As Boolean
        If Not IsNothing(Pandora) Then
            If Not IsNothing(Pandora.Session) Then
                Return True
            Else
                Return False
            End If
        Else
            Return False
        End If
    End Function
    Private Function LoginToPandora() As Boolean
        Try

            If Pandora.Login(My.Settings.pandoraUsername, My.Settings.pandoraPassword) Then
                MakeLogEntry("Successfully logged in to pandora...")
                Return True
            Else
                MakeLogEntry("Couldn't log in to Pandora. Check pandora a/c details.")
            End If
        Catch ex As PandoraException
            If ex.ErrorCode = ErrorCodeEnum.LISTENER_NOT_AUTHORIZED Then
                MakeLogEntry(ex.Message)
            Else
                MakeLogEntry(ex.Message + ". Please check your internet/proxy settings and try again." + vbCrLf + vbCr + "Error Code: " + ex.ErrorCode.ToString)
            End If
        End Try
        Pandora.ClearSession(My.Settings.pandoraOne)
        Return False
    End Function
    Private Sub WaitForNetConnection()

        Dim noNet As Boolean
        Dim sw As New Stopwatch
        sw.Start()
        Do Until NetConnectionAvailable()
            If sw.ElapsedMilliseconds > 10000 Then

                MakeLogEntry("Sorry, but it looks like your internet is down. Please try again later...")
                noNet = True
                Exit Do
            End If
            Threading.Thread.Sleep(1000)
        Loop
        sw.Stop()
        If noNet Then [Stop]()

    End Sub
    Private Function NetConnectionAvailable() As Boolean
        If My.Computer.Network.IsAvailable Then
            If My.Computer.Network.Ping("8.8.8.8") Then
                Return True
            End If
        End If
        Return False
    End Function
    Private Sub RestorePandoraObject()
        If File.Exists("api.dat") Then
            Try
                Using stream As Stream = File.Open("api.dat", FileMode.Open, FileAccess.Read)
                    Dim formatter As New BinaryFormatter()
                    Pandora = DirectCast(formatter.Deserialize(stream), API)
                    MakeLogEntry("Restored the api object from disk...")
                    ServicePointManager.Expect100Continue = False
                End Using
                Exit Sub
            Catch e As Exception
                File.Delete("api.dat")
                MakeLogEntry("Failed to restore the api object from disk...")
            End Try
        End If
        Pandora = New API(My.Settings.pandoraOne)
    End Sub

    Sub MakeLogEntry(Msg As String)
        Console.WriteLine(Msg)
        File.AppendAllText("TandoraTCP.log", Date.Now.ToString() & ": " & Msg & vbCrLf)
    End Sub

    Protected Overrides Sub OnStop()
        ' Add code here to perform any tear-down necessary to stop your service.
    End Sub


End Class
