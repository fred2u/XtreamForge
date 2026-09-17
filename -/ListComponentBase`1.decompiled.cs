using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.CompilerServices;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.FluentUI.AspNetCore.Components.Extensions;
using Microsoft.FluentUI.AspNetCore.Components.Utilities;
using Microsoft.JSInterop;
using __Blazor.Microsoft.FluentUI.AspNetCore.Components.ListComponentBase;

namespace Microsoft.FluentUI.AspNetCore.Components;

/// <summary>
/// Component that provides a list of options.
/// </summary>
/// <typeparam name="TOption"></typeparam>
public abstract class ListComponentBase<TOption> : FluentInputBase<string?>, IAsyncDisposable, IStringParsableComponent where TOption : notnull
{
	private const string JAVASCRIPT_FILE = "./_content/Microsoft.FluentUI.AspNetCore.Components/Components/List/ListComponentBase.razor.js";

	private bool _multiple;

	private bool _hasInitializedParameters;

	protected string? _internalValue;

	protected List<TOption> _selectedOptions = new List<TOption>();

	protected TOption? _currentSelectedOption;

	protected readonly RenderFragment _renderOptions;

	internal InternalListContext<TOption> _internalListContext;

	/// <summary />
	[Inject]
	private LibraryConfiguration LibraryConfiguration { get; set; }

	private IJSObjectReference? _jsModule { get; set; }

	[Inject]
	private IJSRuntime JSRuntime { get; set; }

	internal override bool FieldBound
	{
		get
		{
			if (!base.Field.HasValue && base.ValueExpression == null && !base.ValueChanged.HasDelegate && !SelectedOptionChanged.HasDelegate && SelectedOptionExpression == null && !SelectedOptionsChanged.HasDelegate)
			{
				return SelectedOptionsExpression != null;
			}
			return true;
		}
	}

	/// <summary />
	protected override string? StyleValue => new StyleBuilder(Style).AddStyle("width", Width, !string.IsNullOrEmpty(Width)).Build();

	protected string? InternalValue
	{
		get
		{
			return GetOptionValue(SelectedOption) ?? _internalValue;
		}
		set
		{
			if (value != null && OptionValue != null && Items != null)
			{
				TOption val = Items.FirstOrDefault((TOption i) => GetOptionValue(i) == value);
				if (val != null && !object.Equals(val, SelectedOption))
				{
					SelectedOption = val;
					Value = value;
					RaiseChangedEventsAsync().ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			_internalValue = value;
		}
	}

	/// <summary>
	/// Gets or sets the width of the component.
	/// </summary>
	[Parameter]
	public string? Width { get; set; }

	/// <summary>
	/// Gets or sets the height of the component or of the popup panel.
	/// </summary>
	[Parameter]
	public string? Height { get; set; }

	/// <summary>
	/// Gets or sets the text used on aria-label attribute.
	/// </summary>
	[Parameter]
	[Obsolete("Use AriaLabel instead")]
	public virtual string? Title { get; set; }

	/// <summary>
	/// Gets or sets the content to be rendered inside the component.
	/// In this case list of FluentOptions
	/// </summary>
	[Parameter]
	public virtual RenderFragment? ChildContent { get; set; }

	/// <summary>
	/// Gets or sets the function used to determine which text to display for each option.
	/// </summary>
	[Parameter]
	public virtual Func<TOption, string?> OptionText { get; set; }

	/// <summary>
	/// Gets or sets the function used to determine which value to return for the selected item.
	/// Only for <see cref="T:Microsoft.FluentUI.AspNetCore.Components.FluentListbox`1" /> and <see cref="T:Microsoft.FluentUI.AspNetCore.Components.FluentSelect`1" /> components.
	/// </summary>
	[Parameter]
	public virtual Func<TOption, string?>? OptionValue { get; set; }

	/// <summary>
	/// Gets or sets the function used to determine if an option is disabled.
	/// </summary>
	[Parameter]
	public virtual Func<TOption, bool>? OptionDisabled { get; set; }

	/// <summary>
	/// Gets or sets the function used to determine if an option is initially selected.
	/// </summary>
	[Parameter]
	public virtual Func<TOption, bool>? OptionSelected { get; set; }

	/// <summary>
	/// Gets or sets the function used to determine the option tooltip (title).
	/// If null is returned, then no title is displayed.
	/// </summary>
	[Parameter]
	public virtual Func<TOption, string?>? OptionTitle { get; set; }

	/// <summary>
	/// Gets or sets the <see cref="T:System.Collections.Generic.IEqualityComparer`1" /> used to determine if an option is already added to the internal list.
	/// ⚠️ Only available when Multiple = true.
	/// </summary>
	[Parameter]
	public virtual IEqualityComparer<TOption>? OptionComparer { get; set; }

	/// <summary>
	/// Gets or sets the content source of all items to display in this list.
	/// Each item must be instantiated (cannot be null).
	/// </summary>
	[Parameter]
	public virtual IEnumerable<TOption>? Items { get; set; }

	/// <summary>
	/// Gets or sets the selected item.
	/// ⚠️ Only available when Multiple = false.
	/// </summary>
	[Parameter]
	public virtual TOption? SelectedOption { get; set; }

	/// <summary>
	/// Called whenever the selection changed.
	/// ⚠️ Only available when Multiple = false.
	/// </summary>
	[Parameter]
	public virtual EventCallback<TOption?> SelectedOptionChanged { get; set; }

	/// <summary>
	/// Gets or sets an expression that identifies the bound selected options.
	/// ⚠️ Only available when Multiple = false.
	/// </summary>
	[Parameter]
	public Expression<Func<TOption>>? SelectedOptionExpression { get; set; }

	/// <summary>
	/// If true, the user can select multiple elements.
	/// ⚠️ Only available for the FluentSelect and FluentListbox components.
	/// </summary>
	[Parameter]
	public virtual bool Multiple { get; set; }

	/// <summary>
	/// Gets or sets the template for the <see cref="P:Microsoft.FluentUI.AspNetCore.Components.ListComponentBase`1.Items" /> items.
	/// </summary>
	[Parameter]
	public virtual RenderFragment<TOption>? OptionTemplate { get; set; }

	/// <summary>
	/// Gets or sets all selected items.
	/// ⚠️ Only available when Multiple = true.
	/// </summary>
	[Parameter]
	public virtual IEnumerable<TOption>? SelectedOptions { get; set; }

	/// <summary>
	/// Gets or sets whether using the up and down arrow keys should change focus and select immediately or that selection is done only on Enter.
	/// ⚠️ Only applicable in single select scenarios.
	/// </summary>
	[Parameter]
	public virtual bool ChangeOnEnterOnly { get; set; }

	/// <summary>
	/// Called whenever the selection changed.
	/// ⚠️ Only available when Multiple = true.
	/// </summary>
	[Parameter]
	public virtual EventCallback<IEnumerable<TOption>?> SelectedOptionsChanged { get; set; }

	/// <summary>
	/// Gets or sets an expression that identifies the bound selected options.
	/// ⚠️ Only available when Multiple = true.
	/// </summary>
	[Parameter]
	public Expression<Func<IEnumerable<TOption>>>? SelectedOptionsExpression { get; set; }

	/// <inheritdoc />
	[Parameter]
	public string ParsingErrorMessage { get; set; } = "The {0} field must be a (valid) number.";

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (firstRender)
		{
			IJSObjectReference jsModule = _jsModule;
			IJSObjectReference iJSObjectReference = jsModule;
			if (iJSObjectReference == null)
			{
				_jsModule = await JSRuntimeExtensions.InvokeAsync<IJSObjectReference>(JSRuntime, "import", new object[1] { "./_content/Microsoft.FluentUI.AspNetCore.Components/Components/List/ListComponentBase.razor.js".FormatCollocatedUrl(LibraryConfiguration) });
			}
		}
	}

	/// <summary />
	protected ListComponentBase()
	{
		_internalListContext = new InternalListContext<TOption>(this);
		base.Id = Identifier.NewId();
		OptionText = delegate(TOption item)
		{
			object obj = item?.ToString();
			if (obj == null)
			{
				obj = null;
			}
			return (string?)obj;
		};
		_renderOptions = RenderOptions;
	}

	public override async Task SetParametersAsync(ParameterView parameters)
	{
		parameters.SetParameterProperties(this);
		if (!Multiple)
		{
			bool flag = false;
			bool isSetValue = false;
			TOption val = default(TOption);
			string newValue = null;
			foreach (ParameterValue item in parameters)
			{
				switch (item.Name)
				{
				case "SelectedOption":
					flag = true;
					val = (TOption)item.Value;
					break;
				case "Value":
					isSetValue = true;
					newValue = (string)item.Value;
					break;
				case "Items":
					if (Items != null && OptionSelected != null)
					{
						val = Items.FirstOrDefault((TOption i) => OptionSelected?.Invoke(i) ?? false);
						newValue = GetOptionValue(val);
					}
					break;
				}
			}
			if (val != null || newValue != null || Value != null)
			{
				if (flag && !object.Equals(_currentSelectedOption, val))
				{
					if (Items != null)
					{
						if (Items.Contains(val))
						{
							_currentSelectedOption = val;
							Value = GetOptionValue(_currentSelectedOption);
							await base.ValueChanged.InvokeAsync(Value);
						}
						else
						{
							ListComponentBase<TOption> listComponentBase = this;
							TOption currentSelectedOption = (SelectedOption = default(TOption));
							listComponentBase._currentSelectedOption = currentSelectedOption;
							Value = null;
							await SelectedOptionChanged.InvokeAsync(SelectedOption);
						}
					}
					else
					{
						_currentSelectedOption = val;
					}
				}
				if (isSetValue && newValue == null)
				{
					if (Items != null)
					{
						val = Items.FirstOrDefault((TOption item) => OptionSelected?.Invoke(item) ?? false);
						if (val != null)
						{
							ListComponentBase<TOption> listComponentBase2 = this;
							TOption currentSelectedOption = (SelectedOption = val);
							listComponentBase2._currentSelectedOption = currentSelectedOption;
							newValue = GetOptionValue(_currentSelectedOption);
						}
					}
					if (newValue == null)
					{
						ListComponentBase<TOption> listComponentBase3 = this;
						TOption currentSelectedOption = (SelectedOption = default(TOption));
						listComponentBase3._currentSelectedOption = currentSelectedOption;
						if (!(this is FluentCombobox<TOption>))
						{
							Value = null;
							await base.ValueChanged.InvokeAsync(Value);
						}
					}
					else
					{
						Value = newValue;
						await base.ValueChanged.InvokeAsync(Value);
					}
					await SelectedOptionChanged.InvokeAsync(SelectedOption);
				}
			}
		}
		if (!_hasInitializedParameters)
		{
			if (SelectedOptionExpression != null)
			{
				base.FieldIdentifier = FieldIdentifier.Create(SelectedOptionExpression);
			}
			else if (SelectedOptionChanged.HasDelegate)
			{
				base.FieldIdentifier = FieldIdentifier.Create(() => SelectedOption);
			}
			else if (SelectedOptionsExpression != null)
			{
				base.FieldIdentifier = FieldIdentifier.Create(SelectedOptionsExpression);
			}
			else if (SelectedOptionsChanged.HasDelegate)
			{
				base.FieldIdentifier = FieldIdentifier.Create(() => SelectedOptions);
			}
			_hasInitializedParameters = true;
		}
		await base.SetParametersAsync(ParameterView.Empty);
	}

	protected override void OnInitialized()
	{
		if (_multiple != Multiple)
		{
			if (!(this is FluentListbox<TOption>) && !(this is FluentSelect<TOption>) && !(this is FluentAutocomplete<TOption>))
			{
				throw new ArgumentException("Only FluentSelect, FluentListbox and FluentAutocomplete components support multi-selection mode. ", "Multiple");
			}
			_multiple = Multiple;
		}
		if (!string.IsNullOrEmpty(Height) && string.IsNullOrEmpty(base.Id))
		{
			base.Id = Identifier.NewId();
		}
	}

	protected override void OnParametersSet()
	{
		if (!(this is FluentListbox<TOption>) || Items == null)
		{
			if (!_internalListContext.ValueChanged.HasDelegate)
			{
				_internalListContext.ValueChanged = base.ValueChanged;
			}
			if (!_internalListContext.SelectedOptionChanged.HasDelegate)
			{
				_internalListContext.SelectedOptionChanged = SelectedOptionChanged;
			}
		}
		if (!Multiple && !string.IsNullOrWhiteSpace(Value) && (InternalValue == null || InternalValue != Value))
		{
			InternalValue = Value;
		}
		if (Multiple)
		{
			IEnumerable<TOption>? selectedOptions = SelectedOptions;
			if (selectedOptions != null && !selectedOptions.Any() && _selectedOptions.Count > 0)
			{
				_selectedOptions = new List<TOption>();
			}
			if (SelectedOptions != null && SelectedOptions.Any() && _selectedOptions != SelectedOptions)
			{
				_selectedOptions = SelectedOptions.ToList();
			}
			if (SelectedOptions == null && Items != null && OptionSelected != null && _selectedOptions != null)
			{
				_selectedOptions.AddRange(Items.Where((TOption val) => OptionSelected(val) && !_selectedOptions.Contains(val)));
				InternalValue = GetOptionValue(_selectedOptions.FirstOrDefault());
			}
		}
		else if (SelectedOption == null && Value == null && Items != null && OptionSelected != null)
		{
			TOption item = Items.FirstOrDefault((TOption i) => OptionSelected(i));
			string text = (InternalValue = GetOptionValue(item));
			if (text != null && text != Value && base.ValueChanged.HasDelegate)
			{
				base.ValueChanged.InvokeAsync(text);
			}
		}
	}

	/// <inheritdoc />
	protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out string? result, [NotNullWhen(false)] out string? validationErrorMessage)
	{
		return this.TryParseSelectableValueFromString<ListComponentBase<TOption>, string>(value, out result, out validationErrorMessage);
	}

	/// <inheritdoc />
	protected override string? FormatValueAsString(string? value)
	{
		if (value != null && typeof(TOption) == typeof(bool))
		{
			if (!(bool)(object)value)
			{
				return "false";
			}
			return "true";
		}
		if (typeof(TOption) == typeof(bool?))
		{
			if (value == null || !(bool)(object)value)
			{
				return "false";
			}
			return "true";
		}
		return base.FormatValueAsString(value);
	}

	/// <summary />
	protected virtual bool DisabledItem(TOption item)
	{
		return base.Disabled;
	}

	/// <summary />
	protected virtual bool GetOptionSelected(TOption item)
	{
		if (Multiple)
		{
			if (_selectedOptions == null || item == null)
			{
				return false;
			}
			if (OptionSelected != null && _selectedOptions.Contains(item))
			{
				return OptionSelected(item);
			}
			if (OptionValue != null && _selectedOptions != null)
			{
				foreach (TOption selectedOption in _selectedOptions)
				{
					if (GetOptionValue(item) == GetOptionValue(selectedOption))
					{
						return true;
					}
				}
				return false;
			}
			return _selectedOptions?.Contains(item) ?? false;
		}
		if (OptionSelected != null)
		{
			return OptionSelected(item);
		}
		if (SelectedOption == null && string.IsNullOrEmpty(Value))
		{
			return false;
		}
		if (OptionValue != null && SelectedOption != null)
		{
			return GetOptionValue(item) == GetOptionValue(SelectedOption);
		}
		if (!string.IsNullOrEmpty(Value))
		{
			return string.Equals(GetOptionValue(item), Value, StringComparison.Ordinal);
		}
		return object.Equals(item, SelectedOption);
	}

	/// <summary />
	protected virtual string? GetOptionValue(TOption? item)
	{
		if (item != null)
		{
			object obj = OptionValue?.Invoke(item);
			if (obj == null)
			{
				obj = OptionText?.Invoke(item);
				if (obj == null)
				{
					ref TOption reference = ref item;
					TOption val = default(TOption);
					if (val == null)
					{
						val = reference;
						reference = ref val;
						if (val == null)
						{
							return null;
						}
					}
					obj = reference.ToString();
				}
			}
			return (string?)obj;
		}
		return null;
	}

	/// <summary />
	protected virtual string? GetOptionTitle(TOption? item)
	{
		if (item != null && OptionTitle != null)
		{
			return OptionTitle(item);
		}
		return null;
	}

	protected virtual bool? GetOptionDisabled(TOption? item)
	{
		if (item != null && OptionDisabled != null)
		{
			return OptionDisabled(item);
		}
		return null;
	}

	/// <summary />
	protected virtual string? GetOptionText(TOption? item)
	{
		if (item != null)
		{
			return OptionText(item) ?? item.ToString();
		}
		return null;
	}

	/// <summary />
	protected virtual async Task OnSelectedItemChangedHandlerAsync(TOption? item)
	{
		if (base.Disabled || item == null)
		{
			return;
		}
		if (Multiple)
		{
			if (OptionComparer == null && _selectedOptions.Contains(item))
			{
				RemoveSelectedItem(item);
				await RaiseChangedEventsAsync();
			}
			else
			{
				if (OptionComparer != null)
				{
					TOption val = _selectedOptions.Find((TOption x) => OptionComparer.Equals(x, item));
					if (val != null)
					{
						RemoveSelectedItem(val);
						await RaiseChangedEventsAsync();
						goto IL_021b;
					}
				}
				AddSelectedItem(item);
				await RaiseChangedEventsAsync();
			}
			goto IL_021b;
		}
		if (!object.Equals(item, SelectedOption))
		{
			SelectedOption = item;
			InternalValue = GetOptionValue(item);
			await RaiseChangedEventsAsync();
		}
		else if (this is FluentAutocomplete<TOption>)
		{
			SelectedOption = default(TOption);
			InternalValue = GetOptionValue(default(TOption));
			await RaiseChangedEventsAsync();
		}
		return;
		IL_021b:
		if (!object.Equals(item, SelectedOption))
		{
			SelectedOption = item;
		}
	}

	/// <summary />
	protected virtual async Task RaiseChangedEventsAsync()
	{
		if (Multiple)
		{
			if (SelectedOptionsChanged.HasDelegate)
			{
				await SelectedOptionsChanged.InvokeAsync(_selectedOptions);
			}
		}
		else if (SelectedOptionChanged.HasDelegate)
		{
			await SelectedOptionChanged.InvokeAsync(SelectedOption);
		}
		if (FieldBound)
		{
			base.EditContext?.NotifyFieldChanged(base.FieldIdentifier);
		}
		await base.ChangeHandlerAsync(new ChangeEventArgs
		{
			Value = InternalValue
		});
	}

	protected virtual async Task OnKeydownHandlerAsync(KeyboardEventArgs e)
	{
		if (e != null && !Multiple && !e.ShiftKey && !e.AltKey && !e.CtrlKey)
		{
			await Task.Delay(1);
			string id = await JSObjectReferenceExtensions.InvokeAsync<string>(_jsModule, "getAriaActiveDescendant", new object[1] { base.Id });
			FluentOption<TOption> fluentOption = _internalListContext.Options.FirstOrDefault((FluentOption<TOption> i) => i.Id == id);
			if (fluentOption != null && (!ChangeOnEnterOnly || (ChangeOnEnterOnly && e.Code == "Enter")))
			{
				await fluentOption.OnClickHandlerAsync();
			}
		}
	}

	/// <summary />
	protected virtual bool RemoveSelectedItem(TOption? item)
	{
		if (item == null)
		{
			return false;
		}
		if (this is FluentAutocomplete<TOption> && SelectedOption != null)
		{
			SelectedOption = default(TOption);
			InternalValue = GetOptionValue(default(TOption));
		}
		return _selectedOptions.Remove(item);
	}

	/// <summary />
	protected virtual bool RemoveAllSelectedItems()
	{
		_selectedOptions = new List<TOption>();
		return true;
	}

	/// <summary />
	protected virtual void AddSelectedItem(TOption? item)
	{
		if (item != null)
		{
			_selectedOptions.Add(item);
		}
	}

	protected EventCallback<string> OnSelectCallback(TOption? item)
	{
		return EventCallback.Factory.Create(this, (string e) => OnSelectedItemChangedHandlerAsync(item));
	}

	/// <summary />
	protected internal string? GetAriaLabel()
	{
		if (!string.IsNullOrEmpty(AriaLabel))
		{
			return AriaLabel;
		}
		return Title;
	}

	public async ValueTask DisposeAsync()
	{
		try
		{
			if (_jsModule != null)
			{
				await _jsModule.DisposeAsync();
			}
		}
		catch (Exception ex) when (ex is JSDisconnectedException || ex is OperationCanceledException)
		{
		}
	}

	protected override void BuildRenderTree(RenderTreeBuilder __builder)
	{
	}

	private void RenderOptions(RenderTreeBuilder __builder)
	{
		if (!string.IsNullOrEmpty(Placeholder) && this is FluentSelect<TOption>)
		{
			__builder.OpenComponent<FluentOption<TOption>>(0);
			__builder.AddComponentParameter(1, "Value", "");
			__builder.AddComponentParameter(2, "Style", "display:none;");
			__builder.AddComponentParameter(3, "aria-hidden", "true");
			__builder.AddAttribute(4, "ChildContent", (RenderFragment)delegate(RenderTreeBuilder renderTreeBuilder)
			{
				renderTreeBuilder.AddContent(5, Placeholder);
			});
			__builder.CloseComponent();
		}
		if (Items == null)
		{
			__builder.AddContent(6, ChildContent);
			return;
		}
		bool optionItems = typeof(TOption).IsGenericType && typeof(TOption).GetGenericTypeDefinition() == typeof(Option<>);
		foreach (TOption item in Items)
		{
			__builder.OpenComponent<FluentOption<TOption>>(7);
			__builder.AddComponentParameter(8, "Value", RuntimeHelpers.TypeCheck(GetOptionValue(item)));
			__builder.AddComponentParameter(9, "Title", RuntimeHelpers.TypeCheck(GetOptionTitle(item)));
			__builder.AddComponentParameter(10, "Selected", RuntimeHelpers.TypeCheck(GetOptionSelected(item)));
			__builder.AddComponentParameter(11, "Disabled", RuntimeHelpers.TypeCheck(GetOptionDisabled(item) == true));
			__builder.AddComponentParameter(12, "OnSelect", RuntimeHelpers.TypeCheck(EventCallback.Factory.Create(this, OnSelectCallback(item))));
			__builder.AddComponentParameter(13, "aria-selected", GetOptionSelected(item) ? "true" : "false");
			__builder.AddAttribute(14, "ChildContent", (RenderFragment)delegate(RenderTreeBuilder renderTreeBuilder)
			{
				if (OptionTemplate != null)
				{
					renderTreeBuilder.AddContent(15, OptionTemplate(item));
				}
				else
				{
					renderTreeBuilder.AddContent(16, GetOptionText(item));
				}
				if (optionItems)
				{
					IOptionIcon optionIcon = (IOptionIcon)(object)item;
					(Icon, Color?, string)? icon = optionIcon.Icon;
					if (icon.HasValue)
					{
						(Icon, Color?, string) value = icon.Value;
						TypeInference.CreateFluentIcon_0(renderTreeBuilder, 17, 18, value.Item1, 19, value.Item2, 20, value.Item3);
					}
				}
			});
			__builder.CloseComponent();
		}
	}
}
