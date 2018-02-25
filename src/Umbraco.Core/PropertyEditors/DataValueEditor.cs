﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Newtonsoft.Json;
using Umbraco.Core.Composing;
using Umbraco.Core.IO;
using Umbraco.Core.Logging;
using Umbraco.Core.Models;
using Umbraco.Core.Models.Editors;
using Umbraco.Core.PropertyEditors.Validators;
using Umbraco.Core.Services;

namespace Umbraco.Core.PropertyEditors
{
    /// <summary>
    /// Represents a value editor.
    /// </summary>
    public class DataValueEditor : IDataValueEditor
    {
        private string _view;

        /// <summary>
        /// Initializes a new instance of the <see cref="DataValueEditor"/> class.
        /// </summary>
        public DataValueEditor() // for tests, and manifest
        {
            ValueType = ValueTypes.String;
            Validators = new List<IValueValidator>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DataValueEditor"/> class.
        /// </summary>
        public DataValueEditor(string view, params IValueValidator[] validators) // not used
            : this()
        {
            View = view;
            Validators.AddRange(validators);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DataValueEditor"/> class.
        /// </summary>
        public DataValueEditor(DataEditorAttribute attribute)
            : this()
        {
            if (attribute == null) throw new ArgumentNullException(nameof(attribute));

            var view = attribute.View;
            if (string.IsNullOrWhiteSpace(view))
                throw new ArgumentException("The attribute does not specify a view.", nameof(attribute));

            View = view;
            ValueType = attribute.ValueType;
            HideLabel = attribute.HideLabel;
        }

        // fixme kabam!
        // I don't understand the remarks in the code commented out below
        // and then,
        // IPropertyEditor come from a PropertyEditorCollection so they are singletons
        // IValueEditor is the actual value editor used for editing the value,
        //  and it has its own configuration, depending on the datatype, so it
        //  should NOT be a singleton => do NOT cache it in PropertyEditor!

        /// <summary>
        /// Gets or sets the value editor configuration.
        /// </summary>
        public virtual object Configuration { get; set; }

        //private PreValueCollection _preVals;
        //protected PreValueCollection PreValues
        //{
        //    get
        //    {
        //        if (_preVals == null)
        //        {
        //            throw new InvalidOperationException("Pre values cannot be accessed until the Configure method has been called");
        //        }
        //        return _preVals;
        //    }
        //}

        ///// <summary>
        ///// This is called to configure the editor for display with it's prevalues, useful when properties need to change dynamically
        ///// depending on what is in the pre-values.
        ///// </summary>
        ///// <param name="preValues"></param>
        ///// <remarks>
        ///// This cannot be used to change the value being sent to the editor, ConfigureEditor will be called *after* ConvertDbToEditor, pre-values
        ///// should not be used to modify values.
        ///// </remarks>
        //public virtual void ConfigureForDisplay(PreValueCollection preValues)
        //{
        //    _preVals = preValues ?? throw new ArgumentNullException(nameof(preValues));
        //}

        /// <summary>
        /// Gets or sets the editor view.
        /// </summary>
        /// <remarks>
        /// <para>The view can be three things: (1) the full virtual path, or (2) the relative path to the current Umbraco
        /// folder, or (3) a view name which maps to views/propertyeditors/{view}/{view}.html.</para>
        /// </remarks>
        [JsonProperty("view", Required = Required.Always)]
        public string View
        {
            get => _view;
            set => _view = IOHelper.ResolveVirtualUrl(value);
        }

        /// <summary>
        /// The value type which reflects how it is validated and stored in the database
        /// </summary>
        [JsonProperty("valueType")]
        public string ValueType { get; set; }

        /// <summary>
        /// A collection of validators for the pre value editor
        /// </summary>
        [JsonProperty("validation")]
        public List<IValueValidator> Validators { get; private set; }

        // fixme - need to explain and understand these two + what is "overridable pre-values"

        /// <summary>
        /// Returns the validator used for the required field validation which is specified on the PropertyType
        /// </summary>
        /// <remarks>
        /// This will become legacy as soon as we implement overridable pre-values.
        ///
        /// The default validator used is the RequiredValueValidator but this can be overridden by property editors
        /// if they need to do some custom validation, or if the value being validated is a json object.
        /// </remarks>
        public virtual ManifestValidator RequiredValidator => new RequiredManifestValueValidator();

        /// <summary>
        /// Returns the validator used for the regular expression field validation which is specified on the PropertyType
        /// </summary>
        /// <remarks>
        /// This will become legacy as soon as we implement overridable pre-values.
        ///
        /// The default validator used is the RegexValueValidator but this can be overridden by property editors
        /// if they need to do some custom validation, or if the value being validated is a json object.
        /// </remarks>
        public virtual ManifestValidator RegexValidator => new RegexValidator();

        /// <summary>
        /// If this is is true than the editor will be displayed full width without a label
        /// </summary>
        [JsonProperty("hideLabel")]
        public bool HideLabel { get; set; }

        /// <summary>
        /// Set this to true if the property editor is for display purposes only
        /// </summary>
        public virtual bool IsReadOnly => false;

        /// <summary>
        /// Used to try to convert the string value to the correct CLR type based on the DatabaseDataType specified for this value editor
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        internal Attempt<object> TryConvertValueToCrlType(object value)
        {
            //this is a custom check to avoid any errors, if it's a string and it's empty just make it null
            if (value is string s && string.IsNullOrWhiteSpace(s))
                value = null;

            Type valueType;
            //convert the string to a known type
            switch (ValueTypes.ToStorageType(ValueType))
            {
                case ValueStorageType.Ntext:
                case ValueStorageType.Nvarchar:
                    valueType = typeof(string);
                    break;
                case ValueStorageType.Integer:
                    //ensure these are nullable so we can return a null if required
                    //NOTE: This is allowing type of 'long' because I think json.net will deserialize a numerical value as long
                    // instead of int. Even though our db will not support this (will get truncated), we'll at least parse to this.

                    valueType = typeof(long?);

                    //if parsing is successful, we need to return as an Int, we're only dealing with long's here because of json.net, we actually
                    //don't support long values and if we return a long value it will get set as a 'long' on the Property.Value (object) and then
                    //when we compare the values for dirty tracking we'll be comparing an int -> long and they will not match.
                    var result = value.TryConvertTo(valueType);
                    return result.Success && result.Result != null
                        ? Attempt<object>.Succeed((int)(long)result.Result)
                        : result;

                case ValueStorageType.Decimal:
                    //ensure these are nullable so we can return a null if required
                    valueType = typeof(decimal?);
                    break;

                case ValueStorageType.Date:
                    //ensure these are nullable so we can return a null if required
                    valueType = typeof(DateTime?);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return value.TryConvertTo(valueType);
        }

        // fixme - not dealing with variants here!
        //
        // editors should declare whether they support variants, and then we should have a common
        // way of dealing with it, ie of sending and receiving values, etc.
        // eg
        // [ { "value": "hello" }, { "lang": "fr-fr", "value": "bonjour" } ]

        /// <summary>
        /// A method to deserialize the string value that has been saved in the content editor
        /// to an object to be stored in the database.
        /// </summary>
        /// <param name="editorValue"></param>
        /// <param name="currentValue">
        /// The current value that has been persisted to the database for this editor. This value may be usesful for
        /// how the value then get's deserialized again to be re-persisted. In most cases it will probably not be used.
        /// </param>
        /// <returns></returns>
        /// <remarks>
        /// By default this will attempt to automatically convert the string value to the value type supplied by ValueType.
        ///
        /// If overridden then the object returned must match the type supplied in the ValueType, otherwise persisting the
        /// value to the DB will fail when it tries to validate the value type.
        /// </remarks>
        public virtual object ConvertEditorToDb(ContentPropertyData editorValue, object currentValue)
        {
            //if it's json but it's empty json, then return null
            if (ValueType.InvariantEquals(ValueTypes.Json) && editorValue.Value != null && editorValue.Value.ToString().DetectIsEmptyJson())
            {
                return null;
            }

            var result = TryConvertValueToCrlType(editorValue.Value);
            if (result.Success == false)
            {
                Current.Logger.Warn<DataValueEditor>("The value " + editorValue.Value + " cannot be converted to the type " + ValueTypes.ToStorageType(ValueType));
                return null;
            }
            return result.Result;
        }

        /// <summary>
        /// A method used to format the database value to a value that can be used by the editor
        /// </summary>
        /// <param name="property"></param>
        /// <param name="propertyType"></param>
        /// <param name="dataTypeService"></param>
        /// <returns></returns>
        /// <remarks>
        /// The object returned will automatically be serialized into json notation. For most property editors
        /// the value returned is probably just a string but in some cases a json structure will be returned.
        /// </remarks>
        public virtual object ConvertDbToEditor(Property property, PropertyType propertyType, IDataTypeService dataTypeService)
        {
            if (property.GetValue() == null) return string.Empty;

            switch (ValueTypes.ToStorageType(ValueType))
            {
                case ValueStorageType.Ntext:
                case ValueStorageType.Nvarchar:
                    //if it is a string type, we will attempt to see if it is json stored data, if it is we'll try to convert
                    //to a real json object so we can pass the true json object directly to angular!
                    var asString = property.GetValue().ToString();
                    if (asString.DetectIsJson())
                    {
                        try
                        {
                            var json = JsonConvert.DeserializeObject(asString);
                            return json;
                        }
                        catch
                        {
                            //swallow this exception, we thought it was json but it really isn't so continue returning a string
                        }
                    }
                    return asString;
                case ValueStorageType.Integer:
                case ValueStorageType.Decimal:
                    //Decimals need to be formatted with invariant culture (dots, not commas)
                    //Anything else falls back to ToString()
                    var decim = property.GetValue().TryConvertTo<decimal>();
                    return decim.Success
                        ? decim.Result.ToString(NumberFormatInfo.InvariantInfo)
                        : property.GetValue().ToString();
                case ValueStorageType.Date:
                    var date = property.GetValue().TryConvertTo<DateTime?>();
                    if (date.Success == false || date.Result == null)
                    {
                        return string.Empty;
                    }
                    //Dates will be formatted as yyyy-MM-dd HH:mm:ss
                    return date.Result.Value.ToIsoString();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // fixme - the methods below should be replaced by proper property value convert ToXPath usage!

        /// <summary>
        /// Converts a property to Xml fragments.
        /// </summary>
        public IEnumerable<XElement> ConvertDbToXml(Property property, IDataTypeService dataTypeService, ILocalizationService localizationService, bool published)
        {
            published &= property.PropertyType.IsPublishing;

            var nodeName = property.PropertyType.Alias.ToSafeAlias();

            foreach (var pvalue in property.Values)
            {
                var value = published ? pvalue.PublishedValue : pvalue.EditedValue;
                if (value == null || value is string stringValue && string.IsNullOrWhiteSpace(stringValue))
                    continue;

                var xElement = new XElement(nodeName);
                if (pvalue.LanguageId.HasValue)
                {
                    var language = localizationService.GetLanguageById(pvalue.LanguageId.Value);
                    if (language == null) continue; // uh?
                    xElement.Add(new XAttribute("lang", language.IsoCode));
                }
                if (pvalue.Segment != null)
                    xElement.Add(new XAttribute("segment", pvalue.Segment));

                var xValue = ConvertDbToXml(property.PropertyType, value, dataTypeService);
                xElement.Add(xValue);

                yield return xElement;
            }
        }

        /// <summary>
        /// Converts a property value to an Xml fragment.
        /// </summary>
        /// <remarks>
        /// <para>By default, this returns the value of ConvertDbToString but ensures that if the db value type is
        /// NVarchar or NText, the value is returned as a CDATA fragment - elxe it's a Text fragment.</para>
        /// <para>Returns an XText or XCData instance which must be wrapped in a element.</para>
        /// <para>If the value is empty we will not return as CDATA since that will just take up more space in the file.</para>
        /// </remarks>
        public XNode ConvertDbToXml(PropertyType propertyType, object value, IDataTypeService dataTypeService)
        {
            //check for null or empty value, we don't want to return CDATA if that is the case
            if (value == null || value.ToString().IsNullOrWhiteSpace())
            {
                return new XText(ConvertDbToString(propertyType, value, dataTypeService));
            }

            switch (ValueTypes.ToStorageType(ValueType))
            {
                case ValueStorageType.Date:
                case ValueStorageType.Integer:
                case ValueStorageType.Decimal:
                    return new XText(ConvertDbToString(propertyType, value, dataTypeService));
                case ValueStorageType.Nvarchar:
                case ValueStorageType.Ntext:
                    //put text in cdata
                    return new XCData(ConvertDbToString(propertyType, value, dataTypeService));
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Converts a property value to a string.
        /// </summary>
        public virtual string ConvertDbToString(PropertyType propertyType, object value, IDataTypeService dataTypeService)
        {
            if (value == null)
                return string.Empty;

            switch (ValueTypes.ToStorageType(ValueType))
            {
                case ValueStorageType.Nvarchar:
                case ValueStorageType.Ntext:
                    return value.ToXmlString<string>();
                case ValueStorageType.Integer:
                case ValueStorageType.Decimal:
                    return value.ToXmlString(value.GetType());
                case ValueStorageType.Date:
                    //treat dates differently, output the format as xml format
                    var date = value.TryConvertTo<DateTime?>();
                    if (date.Success == false || date.Result == null)
                        return string.Empty;
                    return date.Result.ToXmlString<DateTime>();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
