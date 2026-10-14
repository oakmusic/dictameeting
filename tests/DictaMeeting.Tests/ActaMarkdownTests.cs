using System;
using System.Linq;
using System.Threading;
using System.Windows.Documents;
using Markdig;
using Markdig.Wpf;
using Xunit;

namespace DictaMeeting.Tests;

public class ActaMarkdownTests
{
    [Fact]
    public void MarkdigWpf_CanParseMarkdownWithAdvancedExtensionsToFlowDocument()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var pipeline = new MarkdownPipelineBuilder().UseSupportedExtensions().Build();
                var md = @"# Acta de la Reunión

## Participantes
- **Ana García** (Directora)
- *Carlos López* (Desarrollo)

### Decisiones
1. Primera decisión
2. Segunda decisión

> Esta es una cita importante.

---

| Tarea | Responsable | Estado |
| :--- | :--- | :--- |
| Módulo A | Ana | Completado |
| Módulo B | Carlos | Pendiente |

```csharp
var x = 42;
```

Para más detalles, consultar [DictaMeeting](https://github.com/dictameeting).";

                var flowDoc = Markdig.Wpf.Markdown.ToFlowDocument(md, pipeline);
                Assert.NotNull(flowDoc);
                Assert.True(flowDoc.Blocks.Count > 0);

                var tableBlock = flowDoc.Blocks.OfType<Table>().FirstOrDefault();
                Assert.NotNull(tableBlock);
                Assert.True(tableBlock.RowGroups.Count > 0);

                var viewer = new Markdig.Wpf.MarkdownViewer();
                viewer.Markdown = md;
                Assert.NotNull(viewer.Document);
                Assert.True(viewer.Document.Blocks.Count > 0);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null)
        {
            throw new AggregateException("Error in STA thread rendering FlowDocument", threadEx);
        }
    }

    [Fact]
    public void MarkdigWpf_StylesKeys_AreAccessible()
    {
        Assert.NotNull(Markdig.Wpf.Styles.Heading1StyleKey);
        Assert.NotNull(Markdig.Wpf.Styles.Heading2StyleKey);
        Assert.NotNull(Markdig.Wpf.Styles.Heading3StyleKey);
        Assert.NotNull(Markdig.Wpf.Styles.DocumentStyleKey);
        Assert.NotNull(Markdig.Wpf.Styles.TableStyleKey);
        Assert.NotNull(Markdig.Wpf.Styles.CodeBlockStyleKey);
    }
}
