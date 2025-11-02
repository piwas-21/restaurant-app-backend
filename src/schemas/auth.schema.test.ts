import { loginSchema, staffRegistrationSchema, customerRegistrationSchema } from './auth.schema';

describe('Auth Schemas', () => {

  describe('loginSchema', () => {
    it('should validate correct login data', () => {
      const data = { email: 'test@example.com', password: 'password123' };
      expect(() => loginSchema.parse(data)).not.toThrow();
    });

    it('should invalidate an incorrect email', () => {
      const data = { email: 'not-an-email', password: 'password123' };
      expect(() => loginSchema.parse(data)).toThrow();
    });

    it('should invalidate with a missing password', () => {
        const data = { email: 'test@example.com' };
        expect(() => loginSchema.parse(data)).toThrow();
    });
  });

  describe('staffRegistrationSchema', () => {
    const validData = {
      firstName: 'John',
      lastName: 'Doe',
      email: 'john.doe@example.com',
      password: 'password123',
      confirmPassword: 'password123',
      role: 'Staff'
    };

    it('should validate correct staff registration data', () => {
      expect(() => staffRegistrationSchema.parse(validData)).not.toThrow();
    });

    it('should invalidate if passwords do not match', () => {
      const data = { ...validData, confirmPassword: 'differentpassword' };
      expect(() => staffRegistrationSchema.parse(data)).toThrow();
    });

    it('should invalidate if password is too short', () => {
        const data = { ...validData, password: 'short', confirmPassword: 'short' };
        expect(() => staffRegistrationSchema.parse(data)).toThrow();
    });
  });

  describe('customerRegistrationSchema', () => {
    const validData = {
        firstName: 'Jane',
        lastName: 'Doe',
        email: 'jane.doe@example.com',
        password: 'password123',
        confirmPassword: 'password123',
    };

    it('should validate correct customer registration data', () => {
        expect(() => customerRegistrationSchema.parse(validData)).not.toThrow();
    });

    it('should invalidate if passwords do not match', () => {
        const data = { ...validData, confirmPassword: 'differentpassword' };
        expect(() => customerRegistrationSchema.parse(data)).toThrow();
    });

    it('should invalidate if password is too short', () => {
        const data = { ...validData, password: 'short', confirmPassword: 'short' };
        expect(() => customerRegistrationSchema.parse(data)).toThrow();
    });
  });
});
