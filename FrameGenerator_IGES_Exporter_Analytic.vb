' ================================================================
' FRAME GENERATOR -> IGES EXPORTER (TubesT compatibility)
' Autodesk Inventor 2027 / iLogic
'
' Отличия от исходного правила:
'   - экспорт поверхностями IGES 144 вместо Manifold Solid B-Rep;
'   - эскизы не экспортируются;
'   - перед экспортом выполняется Update2;
'   - допускается ровно одно замкнутое solid-тело;
'   - создаётся подробный IGES_EXPORT_LOG.txt.
' ================================================================

Sub Main()

    Dim nameTemplate As String = "{StockNumber}_{Material}_L{Length}_Q{Qty}"

    ' IGES: 0 = Surfaces, 1 = Solids, 2 = Wireframe.
    ' TubesT зависает на Inventor Manifold Solid B-Rep (IGES 186/514).
    ' Surface-модель с обрезанными гранями IGES 144 открывается корректно.
    Dim geometryType As Integer = 0

    ' IGES solid faces: 0 = NURBS, 1 = Analytic.
    ' Analytic сохраняет плоскости и цилиндры простыми поверхностями и обычно
    ' лучше воспринимается программами раскроя труб.
    Dim solidFaceType As Integer = 1

    ' Используется только если geometryType = 0:
    ' 0 = IGES 143 Bounded, 1 = IGES 144 Trimmed.
    Dim surfaceType As Integer = 1

    ' Внутренние единицы Inventor — сантиметры. 0.001 cm = 0.01 mm.
    Dim exportFitToleranceCm As Double = 0.001

    Dim activeDoc As Document = ThisApplication.ActiveDocument
    If activeDoc Is Nothing Then
        MessageBox.Show("Нет открытого документа.", "Frame IGES Exporter")
        Exit Sub
    End If

    If activeDoc.DocumentType <> DocumentTypeEnum.kAssemblyDocumentObject Then
        MessageBox.Show(
            "Откройте главную сборку .iam и запустите правило ещё раз.",
            "Frame IGES Exporter")
        Exit Sub
    End If

    Dim asmDoc As AssemblyDocument = CType(activeDoc, AssemblyDocument)
    Dim defaultFolder As String
    If Not String.IsNullOrWhiteSpace(asmDoc.FullFileName) Then
        defaultFolder = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(asmDoc.FullFileName), "IGES")
    Else
        defaultFolder = "C:\Temp\IGES"
    End If

    Dim outputFolder As String = SelectOutputFolder(defaultFolder)
    If String.IsNullOrWhiteSpace(outputFolder) Then Exit Sub
    If Not System.IO.Directory.Exists(outputFolder) Then
        System.IO.Directory.CreateDirectory(outputFolder)
    End If

    Dim logLines As New System.Collections.Generic.List(Of String)()
    logLines.Add("Frame Generator IGES Exporter — TubesT compatibility")
    logLines.Add("Time=" & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
    logLines.Add("Assembly=" & asmDoc.FullFileName)
    logLines.Add(
        "Options: GeometryType=" & geometryType &
        ", SolidFaceType=" & solidFaceType &
        ", SurfaceType=" & surfaceType &
        ", IncludeSketches=False" &
        ", ToleranceCm=" & exportFitToleranceCm.ToString(
            "0.#####", System.Globalization.CultureInfo.InvariantCulture))
    logLines.Add("")

    Dim igesTranslator As TranslatorAddIn = Nothing
    Try
        igesTranslator = CType(
            ThisApplication.ApplicationAddIns.ItemById(
                "{90AF7F44-0C01-11D5-8E83-0010B541CD80}"),
            TranslatorAddIn)
        If Not igesTranslator.Activated Then igesTranslator.Activate()
    Catch ex As Exception
        MessageBox.Show(
            "Не удалось получить IGES Translator." & vbCrLf & vbCrLf & ex.Message,
            "Frame IGES Exporter")
        Exit Sub
    End Try

    If igesTranslator Is Nothing Then
        MessageBox.Show("IGES Translator не найден.", "Frame IGES Exporter")
        Exit Sub
    End If

    Dim quantities As Object = CreateObject("Scripting.Dictionary")
    quantities.CompareMode = 1
    Dim exported As Object = CreateObject("Scripting.Dictionary")
    exported.CompareMode = 1
    Dim usedFileNames As Object = CreateObject("Scripting.Dictionary")
    usedFileNames.CompareMode = 1

    CountFrameOccurrences(asmDoc.ComponentDefinition.Occurrences, quantities, logLines)
    If quantities.Count = 0 Then
        MessageBox.Show(
            "В сборке не найдено ни одного элемента Frame Generator.",
            "Frame IGES Exporter")
        Exit Sub
    End If

    Dim exportCount As Integer = 0
    Dim errorCount As Integer = 0

    ProcessOccurrences(
        asmDoc.ComponentDefinition.Occurrences,
        igesTranslator,
        outputFolder,
        nameTemplate,
        geometryType,
        solidFaceType,
        surfaceType,
        exportFitToleranceCm,
        quantities,
        exported,
        usedFileNames,
        exportCount,
        errorCount,
        logLines)

    Dim logPath As String = System.IO.Path.Combine(outputFolder, "IGES_EXPORT_LOG.txt")
    Try
        System.IO.File.WriteAllLines(
            logPath,
            logLines.ToArray(),
            New System.Text.UTF8Encoding(False))
    Catch ex As Exception
        Logger.Info("Не удалось записать журнал IGES: " & ex.Message)
    End Try

    Dim msg As String =
        "Экспорт завершён." & vbCrLf & vbCrLf &
        "Уникальных Frame Members: " & quantities.Count & vbCrLf &
        "Экспортировано IGES: " & exportCount & vbCrLf &
        "Ошибок: " & errorCount & vbCrLf & vbCrLf &
        "Папка:" & vbCrLf & outputFolder & vbCrLf & vbCrLf &
        "Журнал:" & vbCrLf & logPath

    MessageBox.Show(msg, "Frame IGES Exporter")

End Sub


Function SelectOutputFolder(defaultFolder As String) As String
    Try
        Dim dlg As New System.Windows.Forms.FolderBrowserDialog()
        dlg.Description = "Выберите папку для экспорта Frame Generator в IGES"
        dlg.ShowNewFolderButton = True

        If System.IO.Directory.Exists(defaultFolder) Then
            dlg.SelectedPath = defaultFolder
        Else
            Dim parentFolder As String = System.IO.Path.GetDirectoryName(defaultFolder)
            If System.IO.Directory.Exists(parentFolder) Then dlg.SelectedPath = parentFolder
        End If

        If dlg.ShowDialog() = System.Windows.Forms.DialogResult.OK Then
            Return dlg.SelectedPath
        End If
    Catch ex As Exception
        MessageBox.Show(
            "Ошибка окна выбора папки:" & vbCrLf & ex.Message,
            "Frame IGES Exporter")
    End Try
    Return ""
End Function


Sub CountFrameOccurrences(
    occurrences As ComponentOccurrences,
    quantities As Object,
    logLines As System.Collections.Generic.List(Of String))

    If occurrences Is Nothing Then Exit Sub

    For Each occ As ComponentOccurrence In occurrences
        Try
            If occ.Suppressed Then Continue For
            Dim compDoc As Document = occ.Definition.Document

            If compDoc.DocumentType = DocumentTypeEnum.kPartDocumentObject Then
                If IsFrameMember(compDoc) Then
                    Dim key As String = GetDocumentKey(compDoc)
                    If quantities.Exists(key) Then
                        quantities(key) = CInt(quantities(key)) + 1
                    Else
                        quantities.Add(key, 1)
                    End If
                End If
            ElseIf compDoc.DocumentType = DocumentTypeEnum.kAssemblyDocumentObject Then
                CountFrameOccurrences(
                    CType(compDoc, AssemblyDocument).ComponentDefinition.Occurrences,
                    quantities,
                    logLines)
            End If
        Catch ex As Exception
            Dim text As String = "COUNT ERROR | " & occ.Name & " | " & ex.Message
            logLines.Add(text)
            Logger.Info(text)
        End Try
    Next
End Sub


Sub ProcessOccurrences(
    occurrences As ComponentOccurrences,
    igesTranslator As TranslatorAddIn,
    outputFolder As String,
    nameTemplate As String,
    geometryType As Integer,
    solidFaceType As Integer,
    surfaceType As Integer,
    exportFitToleranceCm As Double,
    quantities As Object,
    exported As Object,
    usedFileNames As Object,
    ByRef exportCount As Integer,
    ByRef errorCount As Integer,
    logLines As System.Collections.Generic.List(Of String))

    If occurrences Is Nothing Then Exit Sub

    For Each occ As ComponentOccurrence In occurrences
        Try
            If occ.Suppressed Then Continue For
            Dim compDoc As Document = occ.Definition.Document

            If compDoc.DocumentType = DocumentTypeEnum.kPartDocumentObject Then
                If Not IsFrameMember(compDoc) Then Continue For

                Dim key As String = GetDocumentKey(compDoc)
                If exported.Exists(key) Then Continue For

                Dim qty As Integer = 1
                If quantities.Exists(key) Then qty = CInt(quantities(key))

                ExportFrameMember(
                    CType(compDoc, PartDocument),
                    igesTranslator,
                    outputFolder,
                    nameTemplate,
                    geometryType,
                    solidFaceType,
                    surfaceType,
                    exportFitToleranceCm,
                    qty,
                    usedFileNames,
                    logLines)

                exported.Add(key, True)
                exportCount += 1

            ElseIf compDoc.DocumentType = DocumentTypeEnum.kAssemblyDocumentObject Then
                ProcessOccurrences(
                    CType(compDoc, AssemblyDocument).ComponentDefinition.Occurrences,
                    igesTranslator,
                    outputFolder,
                    nameTemplate,
                    geometryType,
                    solidFaceType,
                    surfaceType,
                    exportFitToleranceCm,
                    quantities,
                    exported,
                    usedFileNames,
                    exportCount,
                    errorCount,
                    logLines)
            End If
        Catch ex As Exception
            errorCount += 1
            Dim text As String =
                "EXPORT ERROR | " & occ.Name & " | " & ex.Message.Replace(vbCrLf, " ")
            logLines.Add(text)
            Logger.Info(text)
        End Try
    Next
End Sub


Sub ExportFrameMember(
    partDoc As PartDocument,
    igesTranslator As TranslatorAddIn,
    outputFolder As String,
    nameTemplate As String,
    geometryType As Integer,
    solidFaceType As Integer,
    surfaceType As Integer,
    exportFitToleranceCm As Double,
    qty As Integer,
    usedFileNames As Object,
    logLines As System.Collections.Generic.List(Of String))

    Dim updateOk As Boolean = partDoc.Update2(False)
    If Not updateOk Then
        Throw New Exception("Update2 сообщил об ошибке модели: " & partDoc.DisplayName)
    End If

    Dim geometrySummary As String = ValidateAndDescribeGeometry(partDoc)

    Dim baseName As String = MakeUniqueName(
        BuildFileName(partDoc, nameTemplate, qty), usedFileNames)
    Dim outputFile As String = System.IO.Path.Combine(outputFolder, baseName & ".igs")

    Dim context As TranslationContext =
        ThisApplication.TransientObjects.CreateTranslationContext
    context.Type = IOMechanismEnum.kFileBrowseIOMechanism

    Dim options As NameValueMap =
        ThisApplication.TransientObjects.CreateNameValueMap

    If Not igesTranslator.HasSaveCopyAsOptions(partDoc, context, options) Then
        Throw New Exception(
            "IGES Translator не предоставляет параметры экспорта для " &
            partDoc.DisplayName)
    End If

    options.Value("GeometryType") = geometryType
    options.Value("SolidFaceType") = solidFaceType
    options.Value("SurfaceType") = surfaceType
    options.Value("IncludeSketches") = False
    options.Value("export_fit_tolerance") = exportFitToleranceCm

    If System.IO.File.Exists(outputFile) Then
        Try
            System.IO.File.Delete(outputFile)
        Catch ex As Exception
            Throw New Exception(
                "Не удалось перезаписать файл: " & outputFile & ". " & ex.Message)
        End Try
    End If

    Dim data As DataMedium = ThisApplication.TransientObjects.CreateDataMedium
    data.FileName = outputFile
    igesTranslator.SaveCopyAs(partDoc, context, options, data)

    Dim fi As New System.IO.FileInfo(outputFile)
    If Not fi.Exists Then
        Throw New Exception("IGES файл не был создан: " & outputFile)
    End If
    If fi.Length = 0 Then
        Throw New Exception("IGES файл имеет размер 0 байт: " & outputFile)
    End If

    Dim line As String =
        "OK | " & partDoc.DisplayName &
        " | " & geometrySummary &
        " | Bytes=" & fi.Length &
        " | " & outputFile
    logLines.Add(line)
    Logger.Info(line)

End Sub


Function ValidateAndDescribeGeometry(partDoc As PartDocument) As String
    Dim definition As PartComponentDefinition = partDoc.ComponentDefinition

    If definition.SurfaceBodies.Count = 0 Then
        Throw New Exception("В детали нет тел: " & partDoc.DisplayName)
    End If

    If definition.HasMultipleSolidBodies Then
        Throw New Exception(
            "В детали несколько solid-тел: " & partDoc.DisplayName &
            ". Для TubesT требуется одно итоговое тело.")
    End If

    Dim solidCount As Integer = 0
    Dim faceCount As Integer = 0
    Dim edgeCount As Integer = 0
    Dim volumeCm3 As Double = 0.0

    For Each body As SurfaceBody In definition.SurfaceBodies
        If body.IsSolid Then
            solidCount += 1
            faceCount += body.Faces.Count
            edgeCount += body.Edges.Count
            Try
                volumeCm3 += body.Volume(0.01)
            Catch
            End Try
        Else
            Throw New Exception(
                "Обнаружено незамкнутое surface-тело: " & partDoc.DisplayName)
        End If
    Next

    If solidCount <> 1 Then
        Throw New Exception(
            "Ожидалось одно solid-тело, найдено " & solidCount &
            ": " & partDoc.DisplayName)
    End If

    Dim unhealthy As New System.Collections.Generic.List(Of String)()
    Try
        For Each feature As PartFeature In definition.Features
            If feature.HealthStatus <> HealthStatusEnum.kUpToDateHealth Then
                unhealthy.Add(feature.Name & "=" & feature.HealthStatus.ToString())
            End If
        Next
    Catch
        ' Состояние тела важнее: некоторые служебные Frame Generator features
        ' могут не перечисляться как обычные PartFeature.
    End Try

    Dim unhealthyText As String = "none"
    If unhealthy.Count > 0 Then unhealthyText = String.Join(",", unhealthy.ToArray())

    Dim geometrySummary As String = "Solids=" & solidCount & _
        ", Faces=" & faceCount & _
        ", Edges=" & edgeCount & _
        ", VolumeCm3=" & volumeCm3.ToString( _
            "0.######", System.Globalization.CultureInfo.InvariantCulture) & _
        ", NonUpToDateFeatures=" & unhealthyText
    Return geometrySummary
End Function


Function IsFrameMember(doc As Document) As Boolean
    Try
        Return doc.DocumentInterests.HasInterest(
            "{AC211AE0-A7A5-4589-916D-81C529DA6D17}")
    Catch
        Return False
    End Try
End Function


Function GetDocumentKey(doc As Document) As String
    Try
        If Not String.IsNullOrWhiteSpace(doc.FullFileName) Then
            Return doc.FullFileName.ToUpperInvariant()
        End If
    Catch
    End Try
    Return doc.DisplayName.ToUpperInvariant()
End Function


Function BuildFileName(partDoc As PartDocument, template As String, qty As Integer) As String
    Dim partNumber As String = GetDesignProperty(partDoc, 5)
    Dim material As String = GetDesignProperty(partDoc, 20)
    Dim description As String = GetDesignProperty(partDoc, 29)
    Dim stockNumber As String = GetDesignProperty(partDoc, 55)
    Dim originalFileName As String = ""

    Try
        If Not String.IsNullOrWhiteSpace(partDoc.FullFileName) Then
            originalFileName = System.IO.Path.GetFileNameWithoutExtension(
                partDoc.FullFileName)
        End If
    Catch
    End Try

    If String.IsNullOrWhiteSpace(partNumber) Then partNumber = originalFileName
    If String.IsNullOrWhiteSpace(stockNumber) Then stockNumber = partNumber

    Dim result As String = template
    result = ReplaceToken(result, "{PartNumber}", partNumber)
    result = ReplaceToken(result, "{StockNumber}", stockNumber)
    result = ReplaceToken(result, "{Material}", material)
    result = ReplaceToken(result, "{Description}", description)
    result = ReplaceToken(result, "{Length}", GetFrameLengthMM(partDoc))
    result = ReplaceToken(result, "{Qty}", qty.ToString())
    result = ReplaceToken(result, "{FileName}", originalFileName)
    result = CleanFileName(result)

    If String.IsNullOrWhiteSpace(result) Then result = CleanFileName(originalFileName)
    If String.IsNullOrWhiteSpace(result) Then result = "FrameMember"
    Return result
End Function


Function GetDesignProperty(doc As Document, propId As Integer) As String
    Try
        Dim props As PropertySet = doc.PropertySets.Item(
            "{32853F0F-3444-11D1-9E93-0060B03C1CA6}")
        Dim prop As Inventor.Property = props.ItemByPropId(propId)
        If prop.Value Is Nothing Then Return ""
        Return CStr(prop.Value).Trim()
    Catch
        Return ""
    End Try
End Function


Function GetFrameLengthMM(partDoc As PartDocument) As String
    Try
        Dim p As Parameter = partDoc.ComponentDefinition.Parameters.Item("G_L")
        Dim valueMM As Double = p.ModelValue * 10.0

        If Math.Abs(valueMM - Math.Round(valueMM)) < 0.001 Then
            Return Math.Round(valueMM).ToString(
                "0", System.Globalization.CultureInfo.InvariantCulture)
        End If

        Return valueMM.ToString(
            "0.##", System.Globalization.CultureInfo.InvariantCulture)
    Catch
        Return "NA"
    End Try
End Function


Function ReplaceToken(source As String, token As String, value As String) As String
    If value Is Nothing Then value = ""
    Return source.Replace(token, value)
End Function


Function CleanFileName(fileName As String) As String
    Dim result As String = fileName.Trim()
    For Each c As Char In System.IO.Path.GetInvalidFileNameChars()
        result = result.Replace(c, "_"c)
    Next
    Do While result.Contains("__")
        result = result.Replace("__", "_")
    Loop
    Return result.Trim(" "c, "_"c, "."c)
End Function


Function MakeUniqueName(desiredName As String, usedFileNames As Object) As String
    Dim candidate As String = desiredName
    Dim index As Integer = 2

    Do While usedFileNames.Exists(candidate.ToUpperInvariant())
        candidate = desiredName & "_" & index.ToString()
        index += 1
    Loop

    usedFileNames.Add(candidate.ToUpperInvariant(), True)
    Return candidate
End Function
