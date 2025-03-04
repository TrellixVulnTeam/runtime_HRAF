// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using RuntimeTypeCache = System.RuntimeType.RuntimeTypeCache;

namespace System.Reflection
{
    internal sealed partial class RuntimeConstructorInfo : ConstructorInfo, IRuntimeMethodInfo
    {
        #region Private Data Members
        private volatile RuntimeType m_declaringType;
        private RuntimeTypeCache m_reflectedTypeCache;
        private string? m_toString;
        private ParameterInfo[]? m_parameters; // Created lazily when GetParameters() is called.
#pragma warning disable CA1823, 414, 169
        private object? _empty1; // These empties are used to ensure that RuntimeConstructorInfo and RuntimeMethodInfo are have a layout which is sufficiently similar
        private object? _empty2;
        private object? _empty3;
#pragma warning restore CA1823, 414, 169
        private IntPtr m_handle;
        private MethodAttributes m_methodAttributes;
        private BindingFlags m_bindingFlags;
        private Signature? m_signature;
        private InvocationFlags m_invocationFlags;

        internal InvocationFlags InvocationFlags
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                InvocationFlags flags = m_invocationFlags;
                if ((flags & InvocationFlags.Initialized) == 0)
                {
                    flags = ComputeAndUpdateInvocationFlags(this, ref m_invocationFlags);
                }
                return flags;
            }
        }
        #endregion

        #region Constructor
        internal RuntimeConstructorInfo(
            RuntimeMethodHandleInternal handle, RuntimeType declaringType, RuntimeTypeCache reflectedTypeCache,
            MethodAttributes methodAttributes, BindingFlags bindingFlags)
        {
            m_bindingFlags = bindingFlags;
            m_reflectedTypeCache = reflectedTypeCache;
            m_declaringType = declaringType;
            m_handle = handle.Value;
            m_methodAttributes = methodAttributes;
        }
        #endregion

        #region NonPublic Methods
        RuntimeMethodHandleInternal IRuntimeMethodInfo.Value => new RuntimeMethodHandleInternal(m_handle);

        internal override bool CacheEquals(object? o) =>
            o is RuntimeConstructorInfo m && m.m_handle == m_handle;

        private Signature Signature
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                [MethodImpl(MethodImplOptions.NoInlining)] // move lazy sig generation out of the hot path
                Signature LazyCreateSignature()
                {
                    Signature newSig = new Signature(this, m_declaringType);
                    Volatile.Write(ref m_signature, newSig);
                    return newSig;
                }

                return m_signature ?? LazyCreateSignature();
            }
        }

        private RuntimeType ReflectedTypeInternal => m_reflectedTypeCache.GetRuntimeType();

        internal BindingFlags BindingFlags => m_bindingFlags;
        #endregion

        #region Object Overrides
        public override string ToString()
        {
            if (m_toString == null)
            {
                var sbName = new ValueStringBuilder(MethodNameBufferSize);

                // "Void" really doesn't make sense here. But we'll keep it for compat reasons.
                sbName.Append("Void ");

                sbName.Append(Name);

                sbName.Append('(');
                AppendParameters(ref sbName, GetParameterTypes(), CallingConvention);
                sbName.Append(')');

                m_toString = sbName.ToString();
            }

            return m_toString;
        }
        #endregion

        #region ICustomAttributeProvider
        public override object[] GetCustomAttributes(bool inherit)
        {
            return CustomAttribute.GetCustomAttributes(this, (typeof(object) as RuntimeType)!);
        }

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            if (attributeType == null)
                throw new ArgumentNullException(nameof(attributeType));

            if (attributeType.UnderlyingSystemType is not RuntimeType attributeRuntimeType)
                throw new ArgumentException(SR.Arg_MustBeType, nameof(attributeType));

            return CustomAttribute.GetCustomAttributes(this, attributeRuntimeType);
        }

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            if (attributeType == null)
                throw new ArgumentNullException(nameof(attributeType));

            if (attributeType.UnderlyingSystemType is not RuntimeType attributeRuntimeType)
                throw new ArgumentException(SR.Arg_MustBeType, nameof(attributeType));

            return CustomAttribute.IsDefined(this, attributeRuntimeType);
        }

        public override IList<CustomAttributeData> GetCustomAttributesData()
        {
            return RuntimeCustomAttributeData.GetCustomAttributesInternal(this);
        }
        #endregion

        #region MemberInfo Overrides
        public override string Name => RuntimeMethodHandle.GetName(this);
        public override MemberTypes MemberType => MemberTypes.Constructor;

        public override Type? DeclaringType => m_reflectedTypeCache.IsGlobal ? null : m_declaringType;

        public sealed override bool HasSameMetadataDefinitionAs(MemberInfo other) => HasSameMetadataDefinitionAsCore<RuntimeConstructorInfo>(other);

        public override Type? ReflectedType => m_reflectedTypeCache.IsGlobal ? null : ReflectedTypeInternal;

        public override int MetadataToken => RuntimeMethodHandle.GetMethodDef(this);
        public override Module Module => GetRuntimeModule();

        internal RuntimeType GetRuntimeType() { return m_declaringType; }
        internal RuntimeModule GetRuntimeModule() { return RuntimeTypeHandle.GetModule(m_declaringType); }
        internal RuntimeAssembly GetRuntimeAssembly() { return GetRuntimeModule().GetRuntimeAssembly(); }
        #endregion

        #region MethodBase Overrides

        // This seems to always returns System.Void.
        internal override Type GetReturnType() { return Signature.ReturnType; }

        internal override ParameterInfo[] GetParametersNoCopy() =>
            m_parameters ??= RuntimeParameterInfo.GetParameters(this, this, Signature);

        public override ParameterInfo[] GetParameters()
        {
            ParameterInfo[] parameters = GetParametersNoCopy();

            if (parameters.Length == 0)
                return parameters;

            ParameterInfo[] ret = new ParameterInfo[parameters.Length];
            Array.Copy(parameters, ret, parameters.Length);
            return ret;
        }

        public override MethodImplAttributes GetMethodImplementationFlags()
        {
            return RuntimeMethodHandle.GetImplAttributes(this);
        }

        public override RuntimeMethodHandle MethodHandle => new RuntimeMethodHandle(this);

        public override MethodAttributes Attributes => m_methodAttributes;

        public override CallingConventions CallingConvention => Signature.CallingConvention;

        private RuntimeType[] ArgumentTypes => Signature.Arguments;

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2059:RunClassConstructor",
            Justification = "This ConstructorInfo instance represents the static constructor itself, so if this object was created, the static constructor exists.")]
        private void InvokeClassConstructor()
        {
            Debug.Assert((InvocationFlags & InvocationFlags.RunClassConstructor) != 0);
            var declaringType = DeclaringType;

            if (declaringType != null)
            {
                RuntimeHelpers.RunClassConstructor(declaringType.TypeHandle);
            }
            else
            {
                RuntimeHelpers.RunModuleConstructor(Module.ModuleHandle);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private object? InvokeWorker(object? obj, BindingFlags invokeAttr, in Span<object?> arguments)
        {
            bool wrapExceptions = (invokeAttr & BindingFlags.DoNotWrapExceptions) == 0;
            return RuntimeMethodHandle.InvokeMethod(obj, arguments, Signature, false, wrapExceptions);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private object InvokeCtorWorker(BindingFlags invokeAttr, in Span<object?> arguments)
        {
            bool wrapExceptions = (invokeAttr & BindingFlags.DoNotWrapExceptions) == 0;
            return RuntimeMethodHandle.InvokeMethod(null, arguments, Signature, true, wrapExceptions)!;
        }

        [RequiresUnreferencedCode("Trimming may change method bodies. For example it can change some instructions, remove branches or local variables.")]
        public override MethodBody? GetMethodBody()
        {
            RuntimeMethodBody? mb = RuntimeMethodHandle.GetMethodBody(this, ReflectedTypeInternal);
            if (mb != null)
                mb._methodBase = this;
            return mb;
        }

        public override bool IsSecurityCritical => true;

        public override bool IsSecuritySafeCritical => false;

        public override bool IsSecurityTransparent => false;

        public override bool ContainsGenericParameters => DeclaringType != null && DeclaringType.ContainsGenericParameters;
        #endregion
    }
}
