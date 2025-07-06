import { loginSchema, staffRegistrationSchema, customerRegistrationSchema, login, registerStaff, registerCustomer } from './utils';

// Mocking fetch
global.fetch = jest.fn();

describe('Auth Utility Schemas', () => {

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
      role: 'cashier'
    };

    it('should validate correct staff registration data', () => {
      expect(() => staffRegistrationSchema.parse(validData)).not.toThrow();
    });

    it('should invalidate if passwords do not match', () => {
      const data = { ...validData, confirmPassword: 'differentpassword' };
      expect(() => staffRegistrationSchema.parse(data)).toThrow("Passwords do not match");
    });

    it('should invalidate if password is too short', () => {
        const data = { ...validData, password: 'short', confirmPassword: 'short' };
        expect(() => staffRegistrationSchema.parse(data)).toThrow("Password must be at least 8 characters long.");
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
        expect(() => customerRegistrationSchema.parse(data)).toThrow("Passwords do not match");
    });

    it('should invalidate if password is too short', () => {
        const data = { ...validData, password: 'short', confirmPassword: 'short' };
        expect(() => customerRegistrationSchema.parse(data)).toThrow("Password must be at least 8 characters long.");
    });
  });
});

describe('Auth API Functions', () => {

    beforeEach(() => {
        (fetch as jest.Mock).mockClear();
    });

    it('login function should call the correct endpoint and return data', async () => {
        const mockResponse = { success: true, data: { token: '123' } };
        (fetch as jest.Mock).mockResolvedValueOnce({
            json: () => Promise.resolve(mockResponse),
        });

        const loginData = { email: 'test@example.com', password: 'password' };
        const result = await login(loginData);

        expect(fetch).toHaveBeenCalledWith(`${process.env.NEXT_PUBLIC_API_URL}/Auth/login`, expect.any(Object));
        expect(result).toEqual(mockResponse);
    });

    it('registerStaff function should call the correct endpoint', async () => {
        const mockResponse = { success: true };
         (fetch as jest.Mock).mockResolvedValueOnce({
            json: () => Promise.resolve(mockResponse),
        });

        const staffData = { firstName: 'staff', lastName: 'test', email: 'staff@test.com', password: 'password123', confirmPassword: 'password123', role: 'server' };
        const token = 'fake-token';
        await registerStaff(staffData, token);

        expect(fetch).toHaveBeenCalledWith(`${process.env.NEXT_PUBLIC_API_URL}/Auth/register/staff`, expect.any(Object));
    });

    it('registerCustomer function should call the correct endpoint', async () => {
        const mockResponse = { success: true };
        (fetch as jest.Mock).mockResolvedValueOnce({
            json: () => Promise.resolve(mockResponse),
        });

        const customerData = { firstName: 'customer', lastName: 'test', email: 'customer@test.com', password: 'password123', confirmPassword: 'password123' };
        await registerCustomer(customerData);

        expect(fetch).toHaveBeenCalledWith(`${process.env.NEXT_PUBLIC_API_URL}/Auth/register/customer`, expect.any(Object));
    });

});
