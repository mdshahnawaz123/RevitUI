using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitUI.ExternalCommand.ParameterFilter;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace RevitUI.UI
{
    public partial class ParamFilter : Window
    {
        private readonly Document _doc;
        private readonly UIDocument _uidoc;

        private readonly ExternalEvent _applyFilterEvent;
        private readonly ParamExternal _paramExternal;

        private readonly ExternalEvent _IsolateEvent;
        private readonly IsolateExternal _IsoExternal;

        // Store full Parameter objects so we can get ElementId later
        private List<Parameter> _currentParameters = new();

        public ParamFilter(Document doc, UIDocument uidoc)
        {
            InitializeComponent();
            _doc = doc;
            _uidoc = uidoc;

            _paramExternal = new ParamExternal();
            _applyFilterEvent = ExternalEvent.Create(_paramExternal);

            _IsoExternal = new IsolateExternal();
            _IsolateEvent = ExternalEvent.Create(_IsoExternal);

            LoadCategories();
        }

        private void LoadCategories()
        {
            // Get all model categories that have elements
            var categories = _doc.Settings.Categories
                .Cast<Category>()
                .Where(c => c.CategoryType == CategoryType.Model && c.AllowsBoundParameters)
                .OrderBy(c => c.Name)
                .ToList();

            CategoryCombo.ItemsSource = categories;
            CategoryCombo.DisplayMemberPath = "Name";
        }

        private void ElementWhenCategoryChanged(object sender, SelectionChangedEventArgs e)
        {
            var cat = CategoryCombo.SelectedItem as Category;
            if (cat == null) return;

            _currentParameters = new List<Parameter>();

            // Grab one instance element to read its parameters
            var instanceElement = new FilteredElementCollector(_doc)
                .OfCategoryId(cat.Id)
                .WhereElementIsNotElementType()
                .FirstOrDefault();

            if (instanceElement != null)
                _currentParameters.AddRange(instanceElement.Parameters.Cast<Parameter>());

            // Grab one type element
            var typeElement = new FilteredElementCollector(_doc)
                .OfCategoryId(cat.Id)
                .WhereElementIsElementType()
                .FirstOrDefault();

            if (typeElement != null)
                _currentParameters.AddRange(typeElement.Parameters.Cast<Parameter>());

            // Deduplicate by parameter name, keep only filterable (string/numeric) ones
            _currentParameters = _currentParameters
                .Where(p => p.Definition != null)
                .GroupBy(p => p.Definition.Name)
                .Select(g => g.First())
                .OrderBy(p => p.Definition.Name)
                .ToList();

            // Display the name in the ComboBox
            ParameterCombo.DisplayMemberPath = "Definition.Name";
            ParameterCombo.ItemsSource = _currentParameters;
        }

        private void OnApplyFilter(object sender, RoutedEventArgs e)
        {
            var selectedCat = CategoryCombo.SelectedItem as Category;
            var selectedParam = ParameterCombo.SelectedItem as Parameter;
            var selectedRule = (RuleCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
            var value = ValueBox.Text?.Trim();

            if (selectedCat == null) { MessageBox.Show("Select a Category."); return; }
            if (selectedParam == null) { MessageBox.Show("Select a Parameter."); return; }
            if (string.IsNullOrEmpty(selectedRule)) { MessageBox.Show("Select an Operator."); return; }
            if (string.IsNullOrWhiteSpace(value)) { MessageBox.Show("Enter a filter value."); return; }

            // Pass data to the external event handler
            _paramExternal.CategoryId = selectedCat.Id;
            _paramExternal.ParameterElementId = selectedParam.Id;
            _paramExternal.RuleOperator = selectedRule;
            _paramExternal.FilterValue = value;

            _applyFilterEvent.Raise();
        }

        private void OnIsolate(object sender, RoutedEventArgs e)
        {
            var selectedCat = CategoryCombo.SelectedItem as Category;
            var selectedParam = ParameterCombo.SelectedItem as Parameter;
            var selectedRule = (RuleCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
            var value = ValueBox.Text?.Trim();

            if (selectedCat == null) { MessageBox.Show("Select a Category."); return; }
            if (selectedParam == null) { MessageBox.Show("Select a Parameter."); return; }
            if (string.IsNullOrEmpty(selectedRule)) { MessageBox.Show("Select an Operator."); return; }
            if (string.IsNullOrWhiteSpace(value)) { MessageBox.Show("Enter a filter value."); return; }

            // Pass the same data the apply handler uses
            _IsoExternal.CategoryId = selectedCat.Id;
            _IsoExternal.ParameterElementId = selectedParam.Id;
            _IsoExternal.RuleOperator = selectedRule;
            _IsoExternal.FilterValue = value;

            _IsolateEvent.Raise();
        }

        private void OnClear(object sender, RoutedEventArgs e)
        {
            CategoryCombo.SelectedIndex = -1;
            ParameterCombo.ItemsSource = null;
            RuleCombo.SelectedIndex = -1;
            ValueBox.Clear();
        }

        private void OnRemoveRule(object sender, RoutedEventArgs e)
        {
            // If you add multiple rules later, remove the selected one here
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}