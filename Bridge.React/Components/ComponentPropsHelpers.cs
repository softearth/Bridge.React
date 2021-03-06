﻿using System;

namespace Bridge.React
{
	/// <summary>
	/// React internals do some monkeying about with props references that will cause problems if the props reference is a Bridge class which does not have
	/// the [ObjectLiteral] attribute on it. The way that the Component and StatelessComponent classes work around this is to wrap props reference in an
	/// object literal since React's meddling is not recursive, it doesn't change any property values on props, it just changes how those top-level
	/// properties are described. This class provides a standard way to wrap the props data. It also performs some magic to extract any "Key"
	/// value from the props, since this must not be tucked away one level deeper as it is a magic React property (for more information
	/// about keyed elements, see https://facebook.github.io/react/docs/multiple-components.html#dynamic-children).
	/// </summary>
	internal static class ComponentPropsHelpers
	{
		[IgnoreGeneric]
		public static WrappedValue<TProps> WrapProps<TProps>(TProps propsIfAny)
		{
			// Try to extract a Key value from the props - it might be a simple "key" value or it might be a property with a "getKey" function or it
			// might be absent altogether
			Union<string, int> keyIfAny = null;
			Action<object> refIfAny = null;
			if (propsIfAny != null)
			{
				// Pre-16, Bridge used to default to camel-casing property names and so a "Key" property would be named "key" and it would have a getter method
				// specified for it (it was possible to override these behaviours using PreserveMemberCase and [Name], [Template] or [FieldProperty] attributes)
				// but 16 has changed things such that the name casing is not changed (by default - this may also be altered using the "conventions" options)
				// and so we can't presume that a "Key" property will result in a JavaScript "key" property (or a "getKey" method).
				Script.Write(@"
					if (propsIfAny.key || (propsIfAny.key === 0)) { // Ensure that a zero key is not considered ""no-key-defined""
						keyIfAny = propsIfAny.key;
					}
					else if (propsIfAny.Key || (propsIfAny.Key === 0)) { // Ensure that a zero key is not considered ""no-key-defined""
						keyIfAny = propsIfAny.Key;
					}
					else if (propsIfAny.getKey && (typeof(propsIfAny.getKey) == ""function"")) {
						var keyIfAnyFromPropertyGetter = propsIfAny.getKey();
						if (keyIfAnyFromPropertyGetter || (keyIfAnyFromPropertyGetter === 0)) { // Ensure that a zero key is not considered ""no-key-defined""
							keyIfAny = keyIfAnyFromPropertyGetter;
						}
						else {
							keyIfAny = undefined;
						}
					}
					else {
						keyIfAny = undefined;
					}

					if (typeof(propsIfAny.ref) === ""function"") {
						refIfAny = propsIfAny.ref;
					}
					else if (typeof(propsIfAny.Ref) === ""function"") {
						refIfAny = propsIfAny.Ref;
					}
					else if (typeof(propsIfAny.getRef) === ""function"") {
						var refIfAnyFromPropertyGetter = propsIfAny.getRef();
						if (typeof(refIfAnyFromPropertyGetter) === ""function"") {
							refIfAny = refIfAnyFromPropertyGetter;
						}
						else {
							refIfAny = undefined;
						}
					}
					else {
						refIfAny = undefined;
					}
				");
			}

			// With the changes in React 15.0.0 (vs 0.14.7), a null Key value will be interpreted AS a key (and will either be ".$null" or ".$undefined")
			// when really we want a null Key to mean NO KEY. Possibly related to https://github.com/facebook/react/issues/2386, but I would have expected
			// to have seen this issue in 0.14 if it was that. The workaround is to return a type of "wrapped props" that doesn't even have a Key property
			// on it if there is no key value to use.
			var wrappedProps = new WrappedValue<TProps> { Value = propsIfAny };
			if (Script.Write<bool>("(typeof({0}) !== 'undefined')", keyIfAny))
				Script.Write("{0}.key = {1}", wrappedProps, keyIfAny);
			if (Script.Write<bool>("(typeof({0}) !== 'undefined')", refIfAny))
				Script.Write("{0}.ref = {1}", wrappedProps, refIfAny);
			return wrappedProps;
		}

		[IgnoreGeneric]
		public static TProps UnWrapValueIfDefined<TProps>(WrappedValue<TProps> wrappedValueIfAny)
		{
			return Script.Write<TProps>("{0} ? {0}.value : null", wrappedValueIfAny);
		}

		/// <summary>
		/// Enabling this option allows for an optimisation in DoPropsReferencesMatch for comparing static anonymous functions that will work for projects that are entirely built
		/// upon compiled-from-Bridge.NET C# but that may incorrectly find functions to be equivalent when they shouldn't (if the functions have been bound to different targets
		/// using JS .bind). As such, this defaults to false and is exposed here only to allow unit tests to try the code out (this class is internal and so not for access by
		/// consumers of the Bridge.React package).
		/// </summary>
		public static bool OptimiseFunctionComparisonsBasedOnSolutionBeingPureBridge = false;

		[IgnoreGeneric]
		public static bool DoPropsReferencesMatch<TProps>(TProps props1, TProps props2)
		{
			if ((props1 == null) && (props2 == null))
				return true;
			else if ((props1 == null) || (props2 == null))
				return false;

			// Cast to object before calling GetType since we're using [IgnoreGeneric] (Bridge 15.7.0 bug workaround) - see http://forums.bridge.net/forum/bridge-net-pro/bugs/3343
			if (((object)props1).GetType() != ((object)props2).GetType())
				return false;

			// Bridge adds various private members that we don't want to consider so we want to try to guess whether we're a Bridge class and then ignore them. Basic classes
			// have $$name and $$fullname properties, which seem pretty specific. However, [ObjectLiteral] types may have $literal or $getType properties, which identify them
			// as "special" object literals. Other [ObjectLiteral] types may have no additional properties - which is good because we can skip any additional magic.
			var optimiseFunctionComparisonsBasedOnSolutionBeingPureBridge = OptimiseFunctionComparisonsBasedOnSolutionBeingPureBridge;
			/*@
			var isBridgeType = (!!props1.$$name && !!props1.$$fullname) || (typeof(props1.$getType) === "function") || (typeof(props1.$literal) === "boolean");
			for (var propName in props1) {
				if (isBridgeType && (propName.substr(0, 1) === "$")) {
					continue;
				}
				var propValue1 = props1[propName];
				var propValue2 = props2[propName];
				if ((propValue1 === propValue2) 
				|| ((propValue1 === null) && (propValue2 === null))
				|| ((typeof(propValue1) === "undefined") && (typeof(propValue2) === "undefined"))) {
					// Very simple cases where the properties match
					continue;
				}
				else if ((propValue1 === null) || (propValue2 === null) || (typeof(propValue1) === "undefined") || (typeof(propValue2) === "undefined")) {
					// Simple cases where one or both of the values are some sort of no-value (but either one of them has a value or they're inconsistent types of no-value,
					// since we'd have caught them above otherwise)
					return false;
				}
				else if ((typeof(propValue1) === "function") && (typeof(propValue2) === "function")) {
					// If they're Bridge-bound functions (which is what the presence of $scope and $method properties indicates), then check whether the underlying $method
					// and $scope references match (if they do then this means that it's the same method bound to the same "this" scope, but the actual function references
					// are not the same since they were the results from two different calls to Bridge.fn.bind)
					if (propValue1.$scope && propValue1.$method && propValue2.$scope && propValue2.$method && (propValue1.$scope === propValue2.$scope)) {
						if (propValue1.$method === propValue2.$method) {
							continue;
						}
						if (propValue1.$method.toString() === propValue2.$method.toString()) {
							// If the bound method is a named function then we can use the cheap reference equality comparison above. This is the ideal case, not only because
							// the comparison is so cheap but also because it means that the function is only declared once. Anonymous functions can't be compared by reference
							// and they have a cost (in terms of creation and in terms of additional GC work) that makes them less desirable. However, if the underlying bound
							// functions are anonymous functions then so long as they have the same content then they may be considered equivalent (since we've already checked
							// the references that they're bound to are the same, above).
							continue;
						}
					}
					else if (optimiseFunctionComparisonsBasedOnSolutionBeingPureBridge && isBridgeType && (propValue1.toString() === propValue2.toString())) {
						// This proposition makes me very nervious - if the functions were created by passing another function through .bind or .apply then they could have
						// different targets and it will not be sufficient to just check their string values. If the code in question is 100% compiled-via-Bridge then this
						// shouldn't happen (because Bridge uses its own binding logic) so one option is to try to guess whether the props type is a Bridge class and then
						// only consider this route if so, on the basis that Bridge code written to integrate with vanilla JavaScript is more likely to use plain objects.
						// However, this is still a leap of faith and it's entirely possible that Bridge-written library code intended for JavaScript would expose object
						// initialisation methods to make the JavaScript cleaner than including direct Bridge-class constructor calls. For now, I'll leave this in but
						// behind a turned-off flag until I convince myself that it's safe (or should be removed entirely).
						return true;
					}
				}
				else if ((typeof(propValue1.equals) === "function") && (propValue1.equals(propValue2) === true)) {
					// If propValue1 has an "equals" implementation then give that a go
					continue;
				}
				return false;
			}
			*/
			return true;
		}
	}
}