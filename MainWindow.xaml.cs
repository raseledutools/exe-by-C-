using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using pdftron;
using pdftron.PDF;

namespace RaselPdfPro
{
    public partial class MainWindow : Window
    {
        private PDFDoc _pdfDoc;
        private PDFViewWPF _pdfView;

        public MainWindow()
        {
            InitializeComponent();
            InitializeApryseEngine();
        }

        private void InitializeApryseEngine()
        {
            // Apryse Engine Start
            PDFNet.Initialize(); 
            
            _pdfView = new PDFViewWPF();
            ViewerContainer.Child = _pdfView;

            // বাই-ডিফল্ট টেক্সট এডিট মোড চালু রাখা
            _pdfView.SetToolMode(PDFViewWPF.ToolMode.e_text_edit); 
        }

        private void OpenPdf_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    _pdfDoc = new PDFDoc(openFileDialog.FileName);
                    _pdfView.SetDoc(_pdfDoc);
                    
                    // UI আপডেট করা
                    FileNameText.Text = "📄 " + Path.GetFileName(openFileDialog.FileName);
                    StatusText.Text = $"Loaded - Pages: {_pdfDoc.GetPageCount()}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading PDF: {ex.Message}");
                }
            }
        }

        private void SavePdf_Click(object sender, RoutedEventArgs e)
        {
            if (_pdfDoc == null) return;

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = "Rasel_Edited_Output.pdf"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                _pdfDoc.Save(saveFileDialog.FileName, SDFDoc.SaveOptions.e_remove_unused);
                MessageBox.Show("অ্যাডভান্সড পিডিএফ সফলভাবে সেভ হয়েছে!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // --- টুল পরিবর্তনের লজিক ---
        private void HandTool_Click(object sender, RoutedEventArgs e)
        {
            if (_pdfView != null)
            {
                // স্ক্রল করার জন্য হ্যান্ড টুল
                _pdfView.SetToolMode(PDFViewWPF.ToolMode.e_pan); 
            }
        }

        private void EditTool_Click(object sender, RoutedEventArgs e)
        {
            if (_pdfView != null)
            {
                // ওয়ার্ডের মতো এডিট করার জন্য এডিট টুল
                _pdfView.SetToolMode(PDFViewWPF.ToolMode.e_text_edit); 
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _pdfDoc?.Close();
            PDFNet.Terminate(); 
            base.OnClosed(e);
        }
    }
}
